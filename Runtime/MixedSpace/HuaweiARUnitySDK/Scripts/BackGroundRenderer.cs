namespace HuaweiARUnitySDK
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using HuaweiARInternal;
    using Common;

    /**
* \if english 
* @brief Renders the device's camera as a background to the attached Unity camera component.
* \else
* @brief 将相机预览渲染到unity的相机背景上。
* \endif
*/
    public class BackGroundRenderer : MonoBehaviour
    {
        private Camera m_camera;
        private ARBackgroundRenderer m_backgroundRenderer;
        private static float[] REAR_QUAD_TEXCOORDS = { 0f, 1f, 0f, 0f, 1f, 1f, 1f, 0f };
        private static float[] FRONT_QUAD_TEXCOORDS = { 0f, 0f, 0f, 1f, 1f, 0f, 1f, 1f };
        private float[] currentUVCoords = REAR_QUAD_TEXCOORDS;
        private float[] transformedUVCoords = new float[0];
        private bool m_DiagLogged = false;

        const string backroundTex = "_MainTex";
        const string leftTopBottom = "_UvLeftTopBottom";
        const string rightTopBottom = "_UvRightTopBottom";

        /**
         * \if english
         * A material used to render the AR background image.
         * \else
         * 渲染相机背景所使用的材质。
         * \endif
         */
        public Material BackGroundMaterial;

        private void Start()
        {
            m_camera = GetComponent<Camera>();
            SessionComponent sessionComponent = FindAnyObjectByType<SessionComponent>();
            if (sessionComponent != null && sessionComponent.Config != null &&
                sessionComponent.Config.CameraLensFacing == ARConfigCameraLensFacing.FRONT)
            {
                currentUVCoords = FRONT_QUAD_TEXCOORDS;
            }

            // 诊断日志：相机背景 Shader(HuaweiAR/ARBackground) 仅在 OpenGLES3 下可用，
            // 若真机图形 API 落到 Vulkan，则 shaderSupported=false，Blit 无输出导致黑屏。
            string shaderName = (BackGroundMaterial != null && BackGroundMaterial.shader != null)
                ? BackGroundMaterial.shader.name : "null";
            bool shaderSupported = BackGroundMaterial != null && BackGroundMaterial.shader != null
                && BackGroundMaterial.shader.isSupported;
            Debug.Log("[BackGroundRenderer] graphicsDeviceType=" + SystemInfo.graphicsDeviceType
                + ", graphicsDeviceVersion=" + SystemInfo.graphicsDeviceVersion
                + ", shader=" + shaderName + ", shaderSupported=" + shaderSupported);
        }

        /**
         * \if english
         * @brief Inherit from Unity Update().
         * \else
         * @brief Unity的Update()。
         * \endif
         */
        public void Update()
        {
            if (BackGroundMaterial != null && ARFrame.TextureIsAvailable())
            {
                BackGroundMaterial.SetTexture(backroundTex, ARFrame.CameraTexture);

                // 加固：首帧或显示几何变化时都必须取到 8 个 UV，避免 transformedUVCoords 为空时越界异常
                if (ARFrame.IsDisplayGeometryChanged() || transformedUVCoords.Length != 8)
                {
                    transformedUVCoords = ARFrame.GetTransformDisplayUvCoords(currentUVCoords);
                }

                if (!m_DiagLogged)
                {
                    m_DiagLogged = true;
                    Texture camTex = ARFrame.CameraTexture;
                    Debug.Log("[BackGroundRenderer] first render: cameraTexture="
                        + (camTex != null ? camTex.GetNativeTexturePtr().ToString() : "null")
                        + " " + (camTex != null ? camTex.width + "x" + camTex.height : "?")
                        + ", screen=" + Screen.width + "x" + Screen.height
                        + ", orient=" + Screen.orientation
                        + ", uvLength=" + transformedUVCoords.Length
                        + ", uv=" + UvToString());
                }

                BackGroundMaterial.SetVector(leftTopBottom, new Vector4(transformedUVCoords[0], transformedUVCoords[1],
                    transformedUVCoords[2], transformedUVCoords[3]));
                BackGroundMaterial.SetVector(rightTopBottom, new Vector4(transformedUVCoords[4], transformedUVCoords[5],
                    transformedUVCoords[6], transformedUVCoords[7]));
                Pose p = ARFrame.GetPose();

                m_camera.transform.position = p.position;
                m_camera.transform.rotation = p.rotation;
                m_camera.projectionMatrix = ARSession.GetProjectionMatrix(m_camera.nearClipPlane, m_camera.farClipPlane);

                if (m_backgroundRenderer == null)
                {
                    m_backgroundRenderer = new ARBackgroundRenderer(m_camera, BackGroundMaterial);
                    m_backgroundRenderer.Mode = ARRenderMode.MaterialAsBackground;
                }
            }
        }


        // 把 8 个变换后 UV 及左右两条边的跨度格式化出来：某条 span≈0 即该边退化
        // (两个角点塌到同一列纹素)，正是“一个像素被拉成竖条 + 对角线接缝”的根因。
        private string UvToString()
        {
            if (transformedUVCoords == null || transformedUVCoords.Length != 8)
            {
                return "(not 8)";
            }

            var u = transformedUVCoords;
            float leftSpan = Mathf.Abs(u[0] - u[2]) + Mathf.Abs(u[1] - u[3]);
            float rightSpan = Mathf.Abs(u[4] - u[6]) + Mathf.Abs(u[5] - u[7]);
            return string.Format(
                "L({0:0.###},{1:0.###})-({2:0.###},{3:0.###}) R({4:0.###},{5:0.###})-({6:0.###},{7:0.###})  Lspan={8:0.####} Rspan={9:0.####}",
                u[0], u[1], u[2], u[3], u[4], u[5], u[6], u[7], leftSpan, rightSpan);
        }
    }
}