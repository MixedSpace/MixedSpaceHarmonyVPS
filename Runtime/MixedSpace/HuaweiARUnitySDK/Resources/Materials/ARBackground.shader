Shader "HuaweiAR/ARBackground"
{
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
		_UvLeftTopBottom ("UV of left corners",Vector) = (0,1,0,0)
		_UvRightTopBottom ("UV of right corners",Vector) = (1,1,1,0)
    }
	
    // For GLES3
    SubShader
    {
        Pass
        {
            ZWrite Off

            GLSLPROGRAM

            #pragma only_renderers gles3

            #ifdef SHADER_API_GLES3
            #extension GL_OES_EGL_image_external_essl3 : require
            #endif

            uniform vec4 _UvLeftTopBottom;
            uniform vec4 _UvRightTopBottom;

            #ifdef VERTEX

            varying vec2 textureCoord;

            void main()
            {
                #ifdef SHADER_API_GLES3
                vec2 uvLeft = mix(_UvLeftTopBottom.xy, _UvLeftTopBottom.zw, gl_MultiTexCoord0.y);
                vec2 uvRight = mix(_UvRightTopBottom.xy, _UvRightTopBottom.zw, gl_MultiTexCoord0.y);
                textureCoord = mix(uvLeft, uvRight, gl_MultiTexCoord0.x);

                gl_Position = gl_ModelViewProjectionMatrix * gl_Vertex;
                #endif
            }

            #endif

            #ifdef FRAGMENT
            varying vec2 textureCoord;
            uniform samplerExternalOES _MainTex;

            void main()
            {
                #ifdef SHADER_API_GLES3
                gl_FragColor = texture(_MainTex, textureCoord);
                #endif
            }

            #endif

            ENDGLSL
        }
    }

    // Editor / desktop preview subshader.
    // The device (OpenHarmony, GLES3) always selects the FIRST subshader above, because it
    // carries `#pragma only_renderers gles3`. This second subshader has no renderer
    // restriction, so it is only picked on non-GLES3 platforms such as the Windows editor
    // (DX11), where the OES camera shader cannot compile and the material would otherwise
    // show as magenta. Its only purpose is editor preview; it never runs on device.
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _UvLeftTopBottom;
            float4 _UvRightTopBottom;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 uvLeft  = lerp(_UvLeftTopBottom.xy, _UvLeftTopBottom.zw, v.uv.y);
                float2 uvRight = lerp(_UvRightTopBottom.xy, _UvRightTopBottom.zw, v.uv.y);
                o.uv = lerp(uvLeft, uvRight, v.uv.x);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }

    FallBack Off
}
