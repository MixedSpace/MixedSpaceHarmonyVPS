// Session configuration, on top of the AREngineBridge.
//
// The old layer allocated an ARConfig object per call and filled it through
// HwArConfig_setX, then handed it to HwArSession_configure. AREngine's C API has
// the same shape, but the bridge keeps the pending config on the session and
// exposes it as a begin / set / commit sequence, so the three adapter methods
// split along those lines: Create() opens it, UpdateNDKConfigWithUnityConfig()
// writes exactly the fields the old code wrote, and the caller commits.
//
// The field list is deliberate. Fields the old code never set - depth mode, pose
// mode, image stream mode, map size - stay at whatever AREngine's own
// ARConfig_Create defaults them to, because setting them here would change
// behaviour no sample asked for.

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    internal class ARConfigBaseAdapter
    {
        public ARConfigBaseAdapter(NDKSession session)
        {
            // The pending config belongs to the session the bridge already holds,
            // so this adapter needs no handle of its own - but NDKSession builds
            // every adapter the same way, so the signature stays.
        }

        /// <summary>
        ///     opens the session's pending configuration
        /// </summary>
        /// <returns>a token for it, or IntPtr.Zero when AREngine refused to begin</returns>
        public IntPtr Create()
        {
            if (!ARBridge.Ok("ARConfig.Begin", AREngineExtended.ConfigBegin()))
            {
                return IntPtr.Zero;
            }
            // Any token but zero will do: the bridge owns the one config the
            // session can have pending, so there is nothing to point at.
            return ARBridge.Handle(0);
        }

        public void Destroy(IntPtr configHandle)
        {
            // A committed or cancelled config is already gone - ConfigCommit
            // destroys the pending config as it configures the session.
        }

        public void UpdateNDKConfigWithUnityConfig(IntPtr configHandle, ARConfigBase arConfig)
        {
            Set("AR type", AREngineExtended.ConfigSetARType(arConfig.GetARType()));
            Set("preview size",
                AREngineExtended.ConfigSetPreviewSize(arConfig.PreviewSize.x, arConfig.PreviewSize.y));
            Set("update mode", AREngineExtended.ConfigSetUpdateMode(
                (AREngine_ARUpdateMode)(int)arConfig.GetUpdateMode()));
            Set("power mode", AREngineExtended.ConfigSetPowerMode(ToBridgePowerMode(arConfig.GetPowerMode())));
            Set("camera preview mode",
                AREngineExtended.ConfigSetCameraPreviewMode(AREngine_ARPreviewMode.ARENGINE_PREVIEW_MODE_ENABLED));
            Set("plane finding mode", AREngineExtended.ConfigSetPlaneFindingMode(
                (AREngine_ARPlaneFindingMode)(int)arConfig.GetPlaneFindingMode()));
            Set("semantic mode",
                AREngineExtended.ConfigSetSemanticMode((AREngine_ARSemanticMode)arConfig.GetConfigSemanticMode()));
            Set("semantic dense mode", AREngineExtended.ConfigSetSemanticDenseMode(
                (AREngine_ARSemanticDenseMode)arConfig.GetConfigSemanticDenseMode()));

            int enableMesh = arConfig.EnableMesh ? 1 : 0;
            if (enableMesh == 1)
            {
                Set("mesh mode", AREngineExtended.ConfigSetMeshMode(AREngine_ARMeshMode.ARENGINE_MESH_MODE_ENABLED));
            }
            if (arConfig.GetARType() == (int)NDKARType.FACE_AR)
            {
                Set("multi face mode",
                    AREngineExtended.ConfigSetMultiFaceMode((AREngine_ARMultiFaceMode)(int)arConfig.FaceMultiMode));
            }
            Set("camera lens facing", AREngineExtended.ConfigSetCameraLensFacing(
                (AREngine_ARCameraLensFacing)(int)arConfig.CameraLensFacing));
            // 下发对焦模式。此前 HwArConfig_setFocusMode 只声明未调用，导致 ARWorldTrackingConfig 默认的
            // FIXED_FOCUS 从未传到原生层，相机停留在自动对焦(真机日志 "ProcessAutoFocusUpdates: Focus mode: 0")。
            // 世界跟踪必须锁定对焦：自动对焦会逐帧改变有效焦距、破坏 VIO 依赖的内参一致性，使 VIO 无法进入
            // tracking(日志 tracking state 恒为 0)，反复走 ProcessVioLostNeedReloc→CheckShake 路径(华为 SLAM 崩溃点)。
            Set("focus mode", AREngineExtended.ConfigSetFocusMode(
                (AREngine_ARFocusMode)(int)arConfig.GetFocusMode()));
        }

        /// <summary>
        ///     maps the SDK's power mode onto AREngine's
        /// </summary>
        /// <remarks>
        ///     the two enums agree on 0 and 1 and then part company: the SDK numbers
        ///     ULTRA_POWER_SAVING 2 and PERFORMANCE_FIRST 3, where AREngine uses 2
        ///     for performance and 11 for ultra saving. Passing the SDK's int
        ///     straight through would turn "save as much power as possible" into
        ///     "spend more", so it goes through a table.
        /// </remarks>
        private static AREngine_ARPowerMode ToBridgePowerMode(ARConfigPowerMode mode)
        {
            switch (mode)
            {
                case ARConfigPowerMode.POWER_SAVING:
                    return AREngine_ARPowerMode.ARENGINE_POWER_MODE_POWER_SAVING;
                case ARConfigPowerMode.ULTRA_POWER_SAVING:
                    return AREngine_ARPowerMode.ARENGINE_POWER_MODE_ULTRA_POWER_SAVING;
                case ARConfigPowerMode.PERFORMANCE_FIRST:
                    return AREngine_ARPowerMode.ARENGINE_POWER_MODE_PERFORMANCE_FIRST;
                default:
                    return AREngine_ARPowerMode.ARENGINE_POWER_MODE_NORMAL;
            }
        }

        /// <summary>
        ///     reports a rejected setting and carries on
        /// </summary>
        /// <remarks>
        ///     the old void imports reported nothing and the session was configured
        ///     anyway, so a device that refuses one option - a tablet with no mesh
        ///     support, say - still gets every other field and runs.
        /// </remarks>
        private static void Set(string what, AREngine_ARStatus status)
        {
            ARBridge.Ok("ARConfig.Set" + what, status);
        }
    }
}
