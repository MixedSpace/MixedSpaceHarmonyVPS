Shader"Custom/GridShader"
{
    Properties
    {
        // 基础颜色（网格之间的底色）
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        // 网格线颜色
        _GridColor ("Grid Color", Color) = (0,0,0,1)
        // 网格线粗细
        _GridThickness ("Grid Thickness", Range(0.001, 0.1)) = 0.01
        // 网格密度（数值越小网格越密）
        _GridDensity ("Grid Density", Float) = 10.0
        // 高光强度（可选）
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // 使用标准光照模型和高光
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        // 声明属性对应的变量
fixed4 _BaseColor;
fixed4 _GridColor;
float _GridThickness;
float _GridDensity;
half _Glossiness;
half _Metallic;

struct Input
{
            // 物体的UV坐标，用于计算网格
	float2 uv_MainTex;
};

        // 核心着色器逻辑
void surf(Input IN, inout SurfaceOutputStandard o)
{
            // 1. 缩放UV坐标，控制网格密度
	float2 gridUV = IN.uv_MainTex * _GridDensity;
            
            // 2. 计算UV的小数部分，用于判断是否在网格线上
	float2 fracUV = frac(gridUV);
            
            // 3. 判断当前像素是否在网格线上（水平或垂直）
            // 小于阈值 或 大于(1-阈值) 都属于网格线区域
	float isGridLine = step(1 - _GridThickness, fracUV.x) + step(fracUV.x, _GridThickness) +
                               step(1 - _GridThickness, fracUV.y) + step(fracUV.y, _GridThickness);
            
            // 4. 如果是网格线则用网格颜色，否则用底色
	fixed4 finalColor = lerp(_BaseColor, _GridColor, isGridLine > 0 ? 1 : 0);
            
            // 5. 赋值给表面着色器输出
	o.Albedo = finalColor.rgb;
	o.Metallic = _Metallic;
	o.Smoothness = _Glossiness;
	o.Alpha = finalColor.a;
}
        ENDCG
    }
FallBack"Diffuse"
}