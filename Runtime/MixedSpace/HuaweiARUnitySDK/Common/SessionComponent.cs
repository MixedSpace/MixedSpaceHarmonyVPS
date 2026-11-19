namespace Common
{
    using HuaweiARUnitySDK;
    using UnityEngine;
    using HuaweiARInternal;
    using UnityEngine.OpenHarmony;
    using System;

    public class SessionComponent : MonoBehaviour
    {
        [Tooltip("config")]
        public ARConfigBase Config;
        public GameObject ToastGo;

        [Header("VPS 精简配置")]
        [Tooltip("勾选后：在创建 AR 会话前关闭所有重负载特性(深度/语义平面/稠密重建/平面检测/光照估计)，" +
            "只保留 6DoF 位姿跟踪与相机图像。VPS 在线定位只需要相机帧+位姿，用不到这些重建特性。" +
            "在 grav:0(华为 AREngine 内部重力通道喂不进数据)的设备上，这些特性会加压本就失稳的 VIO/SLAM 管线，" +
            "显著提高 OS_FFRT SLAM 线程 SIGSEGV 概率。默认不勾选，不影响任何原有示例场景。")]
        public bool MinimalARConfigForVPS = false;

        [Header("非 VPS 精简模式下的示例开关")]
        [Tooltip("原示例由全局 Demo 脚本 Main 单例驱动；SDK 独立编译后不再依赖 Main，改由本组件字段控制。")]
        public bool IsVolumeMeasure = false;
        public bool IsMultiFace = false;


        private bool isFirstConnect = true;//this is used to avoid multiple permission request when it was rejected
        private bool isSessionCreated = false;
        private bool isErrorHappendWhenInit = false;

        private bool isEnableMask = false;

        private string errorMessage = null;
        private bool m_Init;
        // 平滑退出是否已执行，避免 GracefulShutdown 与 OnApplicationQuit 兜底重复停止/释放 native 会话。
        private bool m_shutdownDone = false;

        private GUIStyle bb = new GUIStyle();
        private GUISkin skin;

        private void Awake()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private void Start()
        {
            bb.normal.background = null;
            bb.normal.textColor = new Color(1, 0, 0);
            bb.fontSize = 45;

            skin = Resources.Load("Skins/guiSkin") as GUISkin;

            if (MinimalARConfigForVPS)
            {
                // VPS 精简模式：在线定位只需要相机图像 + 6DoF 位姿，用不到深度/语义/稠密重建/平面检测/光照估计。
                // 关闭这些重负载特性，避免给华为 AREngine 在 grav:0 设备上本就失稳的 VIO/SLAM 管线继续加压。
                // 这里刻意不访问 Main.Instance：SingletonPersistent 会自动创建幻影 Main(IsVolumeMeasure 默认 true)，
                // 反过来把 SemanticDenseMode 强制成 VOLUME，正是 VPS 场景误跑重配置的根因。
                Config.EnableDepth = false;
                Config.EnableMask = false;
                Config.EnableMesh = false;
                Config.SemanticMode = ARConfigSemanticMode.NONE;
                Config.SemanticDenseMode = ARConfigSemanticDenseMode.DISABLED;
                Config.LightingMode = ARConfigLightingMode.DISABLED;
                Config.SetPlaneFindingMode(ARConfigPlaneFindingMode.DISABLE);
                ARDebug.LogInfo("SessionComponent: minimal AR config for VPS applied " +
                    "(depth/mask/mesh/semantic/semanticDense/lighting/planeFinding all disabled)");
            }
            else
            {
                if (IsVolumeMeasure)
                {
                    Config.SemanticDenseMode = ARConfigSemanticDenseMode.VOLUME;
                }
                else
                {
                    Config.SemanticDenseMode = ARConfigSemanticDenseMode.SPACE;
                }
                if (IsMultiFace)
                {
                    Config.FaceMultiMode = ARFaceMultiMode.MULTIFACE_ENABLE;
                }
                else
                {
                    Config.FaceMultiMode = ARFaceMultiMode.MULTIFACE_DISABLE;
                }
            }
            Init();
        }

        void Init()
        {

            if (isFirstConnect)
            {
                _Connect();
                isFirstConnect = false;
            }

            if (Config != null)
            {
                isEnableMask = Config.EnableMask;
            }
        }
        public void Update()
        {
            _AppQuitOnEscape();
            AsyncTask.Update();
            //This function must be called before other Components' Update to ensure the accuracy of AREngine
            ARSession.Update();
        }


        public void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                ARSession.Pause();
            }
            else
            {
                if (!isSessionCreated)
                {
                    Init();
                }
                if (isErrorHappendWhenInit)
                {
                    return;
                }
                try
                {
                    _Connect();
                }
                catch (ARCameraPermissionDeniedException e)
                {
                    ARDebug.LogError("camera permission is denied，" + e.Message);
                    errorMessage = "This app require camera permission";
                    Invoke("_DoQuit", 0.5f);
                }
            }
        }

        public void OnApplicationQuit()
        {
            // 兜底：若未走 GracefulShutdown(系统直接退出/进程被回收)，至少同步 Pause→Stop，
            // 让 native 会话先暂停相机供流再停止，缩小与相机 HAL RequestBuffer 的释放竞争窗口。
            // 已走 GracefulShutdown 时 m_shutdownDone=true 且会话已 STOPPED，
            // ARSessionManager.Pause/Stop 均有状态保护(幂等)，不会重复释放。
            if (!m_shutdownDone)
            {
                ARSession.Pause();
                ARSession.Stop();
            }
            isFirstConnect = true;
            isSessionCreated = false;
        }

        private void _Connect()
        {
            ARDebug.LogInfo("_connect begin");
            const string AR_CAMERA_PERMISSION_NAME = Permission.Camera;
            HarmonyPermissionsRequest.IsPermissionGranted(AR_CAMERA_PERMISSION_NAME, () =>
            {
                if (Config.GetARType() != (int)NDKARType.IMAGE_AR)
                {
                    ARDebug.LogInfo("授权成功");
                    // ToastGo 仅用于展示授权提示 UI，真机上其 prefab 实例引用可能解析为 null。
                    // 必须做空引用保护：否则 ToastGo.SetActive 抛 NullReferenceException，
                    // 会跳过后面的 _ConnectToService() 与 ARSession.Resume()，
                    // 导致 AR 会话一直停在 STOPPED、相机背景拿不到纹理而黑屏。
                    if (ToastGo != null)
                    {
                        ToastGo.SetActive(false);
                    }
                    else
                    {
                        ARDebug.LogWarning("ToastGo is null, skip hiding toast, continue starting AR session");
                    }
                    m_Init = true;
                    try
                    {
                        _ConnectToService();
                        ARSession.Resume();
                        ARDebug.LogInfo("AR service connected and session resumed");
                    }
                    catch (Exception e)
                    {
                        ARDebug.LogError("exception while starting AR session: " + e);
                    }
                }
            }, () =>
            {
                ARDebug.LogInfo("授权失败");
                if (ToastGo != null)
                {
                    ToastGo.SetActive(true);
                }
            });
        }
        private void _ConnectToService()
        {
            // 防重入：会话已建立则直接返回，避免重复 CreateSession/Config/CreateFrame。
            // (重复 CreateSession 的根治在 ARSessionManager.CreateSession 的幂等保护；
            //  这里从源头再挡一道，减少重复配置与 frame handle 泄漏。)
            if (isSessionCreated)
            {
                ARDebug.LogWarning("_ConnectToService ignored: AR session already created.");
                return;
            }
            try
            {
                ARSession.CreateSession();
                isSessionCreated = true;
                ARSession.Config(Config);
                ARSession.CreateFrame();
                ARSession.SetDisplayGeometry(Screen.width, Screen.height);
                ARSession.SetCameraTextureNameAuto();
            }
            catch (ARCameraPermissionDeniedException e)
            {
                isErrorHappendWhenInit = true;
                ARDebug.LogError("camera permission is denied，" + e.Message);
                errorMessage = "This app require camera permission";
                Invoke("_DoQuit", 0.5f);
            }
        }

        private void _AppQuitOnEscape()
        {
            if (Input.GetKey(KeyCode.Escape))
            {
                Invoke("_DoQuit", 0.5f);
            }
        }

        private async void _DoQuit()
        {
            // 退出前先执行平滑停止序列(先断相机消费链→暂停→等待→再停止/释放)，完成后再 Quit。
            await GracefulShutdown();
            Application.Quit();
        }

        // 按崩溃根因(SLAM 停止/释放阶段 BufferQueue use-after-free)重构退出顺序：
        // 必须“先断开相机流消费链路、再释放 native 会话/Surface”，消除 VPS 取图任务与
        // native 释放对相机 Surface/BufferQueue 的并发竞争(相机 HAL 的 RequestBuffer 仍在 IPC 飞行)。
        // 对应方案1的 app 侧 C# 落地：停相机消费 → session.pause → 等流停止 → 停止/销毁 session。
        private async System.Threading.Tasks.Task GracefulShutdown()
        {
            if (m_shutdownDone)
            {
                return;
            }
            m_shutdownDone = true;
            try
            {
                // 1) 先断开 app 侧相机消费链：停 MixedSpaceSession(取消 CTS + await 当前取图/定位任务结束)
                //    与 HWSupport(await 当前相机图像任务结束)，确保没有 app 线程还在 AcquireCameraImage。
                MixedSpace.MixedSpaceSDK ms = FindObjectOfType<MixedSpace.MixedSpaceSDK>();
                if (ms != null)
                {
                    ms.enabled = false;
                    if (ms.SpatialSession != null)
                        await ms.SpatialSession.DispatchAsync(MixedSpace.XR.MixedSpaceSessionCommand.Stop);
                    if (ms.Localization != null)
                        await ms.Localization.ShutdownAsync();
                }
                MixedSpace.XR.HWSupport hw = FindObjectOfType<MixedSpace.XR.HWSupport>();
                if (hw != null)
                {
                    hw.enabled = false;
                    await hw.ShutdownAsync();
                }

                // 2) 暂停 native AR 会话(相机供流暂停)，对应 session.pause()。
                ARSession.Pause();

                // 3) 等待相机流真正停止、IPC 队列中的 RequestBuffer 排空(对应“等待流停止回调确认”)。
                await System.Threading.Tasks.Task.Delay(300);

                // 4) 最后才停止并销毁 native 会话/Surface/BufferQueue(对应“释放Surface→销毁session”)。
                ARSession.Stop();
                if (ARSessionManager.Instance != null && ARSessionManager.Instance.m_ndkSession != null)
                {
                    ARSessionManager.Instance.m_ndkSession.DestroyNative();
                }
                ARDebug.LogInfo("GracefulShutdown done: camera consumers stopped before native session release.");
            }
            catch (Exception e)
            {
                ARDebug.LogError("GracefulShutdown error: " + e);
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(0, Screen.height - 100, 200, 200), errorMessage, bb);
            GUI.skin = skin;
            if (errorMessage != null && GUI.Button(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 100, 400, 200), "Quit"))
            {
                _DoQuit();
            }
        }

        public void ConnectServer()
        {
            if (!m_Init)
            {
                m_Init = true;
                _ConnectToService();
            }
        }

    }
}
