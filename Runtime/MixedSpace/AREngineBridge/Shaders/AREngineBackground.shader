// Camera preview background for the AREngine bridge.
//
// Mirrors the GLES3 external-texture path that Huawei's own Unity SDK uses on
// OpenHarmony, which is the one proven to work in a native Tuanjie build. The
// first SubShader carries `only_renderers gles3`, so a device always selects it;
// the second has no renderer restriction and exists purely so the material does
// not show up magenta in the Windows editor, where an OES sampler cannot compile.

Shader "AREngine/Background"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _UvLeftTopBottom ("UV of left corners", Vector) = (0,1,0,0)
        _UvRightTopBottom ("UV of right corners", Vector) = (1,1,1,0)
    }

    // Device path: OpenHarmony builds are GLES3-only.
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
                // No gamma adjustment here on purpose: the preview is already
                // display-referred and the project runs in Linear space, which is
                // what the SDK's shipped shader does too.
                gl_FragColor = texture(_MainTex, textureCoord);
                #endif
            }

            #endif

            ENDGLSL
        }
    }

    // Editor preview only; never selected on a GLES3 device.
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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 uvLeft = lerp(_UvLeftTopBottom.xy, _UvLeftTopBottom.zw, v.uv.y);
                float2 uvRight = lerp(_UvRightTopBottom.xy, _UvRightTopBottom.zw, v.uv.y);
                o.uv = lerp(uvLeft, uvRight, v.uv.x);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }

    FallBack Off
}
