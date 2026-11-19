Shader"Custom/FaceWireframeShader"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _WireframeColor ("Wireframe Color", Color) = (0,0,0,1)
        _WireframeWidth ("Wireframe Width", Range(0.001, 0.05)) = 0.01
        _BaseTransparency ("Base Transparency", Range(0,1)) = 1.0 // 新增：单独控制基础层透明度（可选）
    }
    SubShader
    {
        // 关键修改1：修改渲染队列为透明队列，兼容半透明渲染
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
LOD 100

        // 关键修改2：关闭深度写入（避免透明物体遮挡自身/其他物体）
       ZWrite

Off
        // 关键修改3：开启深度测试（保证渲染顺序正确）
        //ZTestLEqual

        // Pass 1：基础纹理渲染（添加透明度混合）
        Pass
        {
Name"BaseColor"
            // 核心：开启Alpha混合模式，让透明度生效
            Blend

SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

#include "UnityCG.cginc"

struct appdata
{
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f
{
	float2 uv : TEXCOORD0;
	float4 clipPos : SV_POSITION;
};

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _BaseColor;
float _BaseTransparency; // 新增的透明度参数

v2f vert(appdata v)
{
	v2f o;
	o.clipPos = UnityObjectToClipPos(v.vertex);
	o.uv = TRANSFORM_TEX(v.uv, _MainTex);
	return o;
}

fixed4 frag(v2f i) : SV_Target
{
	fixed4 col = tex2D(_MainTex, i.uv) * _BaseColor;
                // 可选：用单独的透明度参数叠加（优先级高于_BaseColor的A）
	col.a *= _BaseTransparency;
	return col;
}
            ENDCG
        }

        // Pass 2：网格线框渲染（微调深度设置，避免线框被透明底色遮挡）
        Pass
        {
Name"Wireframe"
            Blend

SrcAlpha OneMinusSrcAlpha
            // 关键：线框Pass也关闭深度写入，保持和基础层一致
ZWrite Off

ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom
#include "UnityCG.cginc"

struct appdata
{
	float4 vertex : POSITION;
};

struct v2g
{
	float4 clipPos : SV_POSITION;
};

struct g2f
{
	float4 clipPos : SV_POSITION;
	float3 barycentric : TEXCOORD0;
};

float4 _WireframeColor;
float _WireframeWidth;

v2g vert(appdata v)
{
	v2g o;
	o.clipPos = UnityObjectToClipPos(v.vertex);
	return o;
}

[maxvertexcount(3)]
            void geom(triangle v2g input[3], inout TriangleStream<g2f> output)
{
	float3 barys[3] =
	{
		float3(1, 0, 0),
                    float3(0, 1, 0),
                    float3(0, 0, 1)
	};

	for (int i = 0; i < 3; i++)
	{
		g2f o;
		o.clipPos = input[i].clipPos;
		o.barycentric = barys[i];
		output.Append(o);
	}
}

fixed4 frag(g2f i) : SV_Target
{
	float3 dist = smoothstep(0, _WireframeWidth, i.barycentric);
	float edge = min(min(dist.x, dist.y), dist.z);
	float opacity = 1 - edge;
                
	fixed4 col = _WireframeColor;
	col.a *= opacity;
	return col;
}
            ENDCG
        }
    }
FallBack"Transparent/Diffuse" // 降级为透明漫反射
}