using UnityEngine;

namespace MixedSpace
{
    /// <summary>
    /// Reusable startup policy, not a live service container. It contains no token,
    /// scene reference, UI callback or platform implementation. Each SDK receives
    /// detached settings so changing one session cannot mutate the shared asset.
    /// </summary>
    [CreateAssetMenu(menuName = "MixedSpace/Run Plan", fileName = "MixedSpaceRunPlan")]
    public sealed class MixedSpaceRunPlan : ScriptableObject
    {
        [SerializeField] private bool m_StartAutomatically = true;
        [SerializeField, Tooltip("写入定位图像、位姿、融合和时间戳诊断文件。World 输出可能逐帧写入；重新启动运行时后生效。\nWrite localization images and pose, fusion and timestamp diagnostics to disk. World output may write every frame. Restart the runtime to apply.")]
        private bool m_WritePoseLogs;
        // Inspector 只显示距离和旋转纠偏阈值；其余字段仍会序列化并写入融合配置。
        // The Inspector exposes distance and rotation refinement. All fields remain serialized and included in the fusion config.
        [SerializeField, HideInInspector, Min(0f), Tooltip("PoseFusion 绕 X 轴的角度阈值，单位为度。\nPoseFusion x-axis angle threshold in degrees.")]
        private float m_PoseFusionXAxisAngleThreshold;
        [SerializeField, HideInInspector, Min(0f), Tooltip("PoseFusion 绕 Z 轴的角度阈值，单位为度。\nPoseFusion z-axis angle threshold in degrees.")]
        private float m_PoseFusionZAxisAngleThreshold;
        [SerializeField, Min(0f), Tooltip("PoseFusion 距离纠偏阈值，单位为米。\nPoseFusion distance threshold for refinement in meters.")]
        private float m_PoseFusionRefineThresholdDist = 0.2f;
        [SerializeField, HideInInspector, Min(0f), Tooltip("PoseFusion 判断位姿无效的平移差阈值，单位为米。\nPoseFusion translation difference threshold for invalid poses in meters.")]
        private float m_PoseFusionDiffTransThreshold = 0.5f;
        [SerializeField, Min(0f), Tooltip("PoseFusion 旋转纠偏阈值，单位为度。\nPoseFusion rotation threshold for refinement in degrees.")]
        private float m_PoseFusionRefineThresholdAngle = 1f;
        [SerializeField, HideInInspector, Min(0f), Tooltip("PoseFusion 判断位姿无效的旋转差阈值，单位为度。\nPoseFusion rotation difference threshold for invalid poses in degrees.")]
        private float m_PoseFusionDiffAngleThreshold = 5f;
        [SerializeField, HideInInspector, Tooltip("启用 3DoF，仅使用 SLAM 旋转。\nEnable 3DoF and use only SLAM rotation.")]
        private bool m_PoseFusionEnableThreeDof;
        [SerializeField] private MixedSpaceLocalizationMode m_LocalizationMode = MixedSpaceLocalizationMode.OnDevice;
        [SerializeField] private int m_FrameRate = 60;
        [SerializeField] private MixedSpaceLogger.LoggingLevel m_Diagnostics = MixedSpaceLogger.LoggingLevel.ErrorsAndWarnings;
        [SerializeField] private MixedSpaceSessionOptions m_Scheduling = new MixedSpaceSessionOptions();
        [SerializeField] private MixedSpaceTrackingOptions m_Quality = new MixedSpaceTrackingOptions();
        [SerializeField] private MixedSpaceOnDeviceOptions m_DeviceSearch = new MixedSpaceOnDeviceOptions();
        [SerializeField] private MixedSpaceCloudOptions m_RemoteSearch = new MixedSpaceCloudOptions();
        [SerializeField] private MixedSpaceGeoOptions m_GeographicSearch = new MixedSpaceGeoOptions();

        public bool StartAutomatically => m_StartAutomatically;
        public int FrameRate => m_FrameRate;
        public MixedSpaceLogger.LoggingLevel Diagnostics => m_Diagnostics;

        /// <summary>Creates settings owned exclusively by the requesting runtime.</summary>
        public MixedSpaceRuntimeConfiguration CreateRuntimeSettings()
        {
            var settings = new MixedSpaceRuntimeConfiguration();
            settings.WritePoseLogs = m_WritePoseLogs;
            settings.PoseFusionConfig = new MixedSpacePoseFusionConfig
            {
                x_axis_angle_threshold = m_PoseFusionXAxisAngleThreshold,
                z_axis_angle_threshold = m_PoseFusionZAxisAngleThreshold,
                refine_threshold_dist = m_PoseFusionRefineThresholdDist,
                diff_trans_threshold = m_PoseFusionDiffTransThreshold,
                refine_threshold_angle = m_PoseFusionRefineThresholdAngle,
                diff_angle_threshold = m_PoseFusionDiffAngleThreshold,
                enable_three_dof = m_PoseFusionEnableThreeDof
            };
            settings.LocalizationMode = m_LocalizationMode == 0 ? MixedSpaceLocalizationMode.OnDevice : m_LocalizationMode;
            settings.CopyPoliciesFrom(m_Scheduling, m_DeviceSearch, m_RemoteSearch,
                m_GeographicSearch, m_Quality);
            return settings;
        }

        /// <summary>
        /// Explicit authoring operation. Copies values only; never keeps
        /// the source settings or any of their scene references.
        /// </summary>
        public void Capture(MixedSpaceRuntimeConfiguration source, bool startAutomatically,
            int frameRate, MixedSpaceLogger.LoggingLevel diagnostics)
        {
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            m_WritePoseLogs = source.WritePoseLogs;
            MixedSpacePoseFusionConfig poseFusionConfig = source.PoseFusionConfig;
            m_PoseFusionXAxisAngleThreshold = poseFusionConfig.x_axis_angle_threshold;
            m_PoseFusionZAxisAngleThreshold = poseFusionConfig.z_axis_angle_threshold;
            m_PoseFusionRefineThresholdDist = poseFusionConfig.refine_threshold_dist;
            m_PoseFusionDiffTransThreshold = poseFusionConfig.diff_trans_threshold;
            m_PoseFusionRefineThresholdAngle = poseFusionConfig.refine_threshold_angle;
            m_PoseFusionDiffAngleThreshold = poseFusionConfig.diff_angle_threshold;
            m_PoseFusionEnableThreeDof = poseFusionConfig.enable_three_dof;
            m_LocalizationMode = source.LocalizationMode;
            m_Scheduling = MixedSpaceRuntimeConfiguration.CopyValue(source.Session);
            m_Quality = MixedSpaceRuntimeConfiguration.CopyValue(source.Tracking);
            m_DeviceSearch = MixedSpaceRuntimeConfiguration.CopyValue(source.OnDevice);
            m_RemoteSearch = MixedSpaceRuntimeConfiguration.CopyValue(source.Cloud);
            m_GeographicSearch = MixedSpaceRuntimeConfiguration.CopyValue(source.Geo);
            m_StartAutomatically = startAutomatically;
            m_FrameRate = frameRate;
            m_Diagnostics = diagnostics;
        }
    }
}
