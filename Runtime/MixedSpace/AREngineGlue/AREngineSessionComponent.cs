// AREngine session lifecycle driven through libAREngineBridge.so.
//
// Replaces Common.SessionComponent's calls into the old HuaweiARUnitySDK
// wrappers. The shutdown sequence is carried over unchanged and must stay that
// way: the crash it was written against was a use-after-free on the camera
// HAL's RequestBuffer, where the native session and its Surface were released
// while a localisation task was still inside AcquireCameraImage. Breaking the
// order reintroduces the SIGSEGV.
//
// Unlike the original, nothing here is asynchronous on the native side, so the
// SDK's AsyncTask pump is not needed. AREngineSession.Update issues the render
// thread event AREngine's camera calls require and waits for it, which is why
// this must still run before anything that reads a frame.

using System;
using AREngineBridge;
using HuaweiARUnitySDK;
using UnityEngine;
using UnityEngine.OpenHarmony;

namespace AREngineBridge.Glue
{
    /// <summary>
    ///     creates, resumes, updates and tears down the AREngine session for the scene
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AREngineSessionComponent : MonoBehaviour
    {
        private const string CameraPermission = Permission.Camera;

        [Tooltip("world, image, face or body tracking; despite the name AREngine_ARType is a bitmask")]
        public AREngine_ARType arType = AREngine_ARType.ARENGINE_TYPE_WORLD;

        [Tooltip("which planes to look for")]
        public AREngine_ARPlaneFindingMode planeFindingMode =
            AREngine_ARPlaneFindingMode.ARENGINE_PLANE_FINDING_MODE_HORIZONTAL_AND_VERTICAL;

        [Tooltip("object shown when the camera permission was refused")]
        public GameObject toastGo;

        [Tooltip("勾选后：在创建 AR 会话前关闭所有重负载特性(深度/语义平面/稠密重建/平面检测)，" +
            "只保留 6DoF 位姿跟踪与相机图像。VPS 在线定位只需要相机帧+位姿，用不到这些重建特性。" +
            "在 grav:0(华为 AREngine 内部重力通道喂不进数据)的设备上，这些特性会加压本就失稳的 VIO/SLAM 管线，" +
            "显著提高 OS_FFRT SLAM 线程 SIGSEGV 概率。")]
        public bool minimalARConfigForVPS = false;

        [Header("非 VPS 精简模式下的示例开关")]
        [Tooltip("原示例由全局 Demo 脚本 Main 单例驱动；SDK 独立编译后不再依赖 Main，改由本组件字段控制。")]
        public bool isVolumeMeasure = false;

        public bool isMultiFace = false;

        private bool _isFirstConnect = true;
        private bool _isSessionCreated;
        private bool _initCalled;
        private bool _shutdownDone;
        private string _errorMessage;
        private GUIStyle _errorStyle;

        private void Awake()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private void Start()
        {
            _errorStyle = new GUIStyle
            {
                normal = { background = null, textColor = new Color(1, 0, 0) },
                fontSize = 45,
            };

            Init();
        }

        /// <summary>
        ///     requests the camera permission and, once granted, builds the session
        /// </summary>
        public void Init()
        {
            if (_isFirstConnect)
            {
                Connect();
                _isFirstConnect = false;
            }
        }

        private void Update()
        {
            QuitOnEscape();
            // Must run before anything reads a frame, including AREngineBackground
            // and the localisation pump.
            AREngineSession.Update();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                AREngineSession.Pause();
                return;
            }

            if (!_isSessionCreated)
            {
                Init();
            }

            Connect();
        }

        private void OnApplicationQuit()
        {
            // Fallback for the paths that never reach GracefulShutdown, such as the
            // system reclaiming the process. Pause before Stop so the camera stops
            // serving frames first, narrowing the same RequestBuffer race.
            if (!_shutdownDone)
            {
                AREngineSession.Pause();
                AREngineSession.Stop();
            }

            _isFirstConnect = true;
            _isSessionCreated = false;
        }

        private void Connect()
        {
            HarmonyPermissionsRequest.IsPermissionGranted(CameraPermission, OnPermissionGranted,
                OnPermissionDenied);
        }

        private void OnPermissionGranted()
        {
            if (toastGo != null)
            {
                toastGo.SetActive(false);
            }
            else
            {
                Debug.LogWarning("arengine: toastGo is null, skipping the permission prompt and starting anyway");
            }

            _initCalled = true;
            ConnectToService();
        }

        private void OnPermissionDenied()
        {
            Debug.LogWarning("arengine: camera permission refused");
            _errorMessage = "This app require camera permission";
            if (toastGo != null)
            {
                toastGo.SetActive(true);
            }
        }

        private void ConnectToService()
        {
            // Re-entrancy guard. AREngineSession.Create refuses a second create, but
            // stopping here also keeps Resume and the texture bind from running twice.
            if (_isSessionCreated)
            {
                Debug.LogWarning("arengine: ConnectToService ignored, the session already exists");
                return;
            }

            AREngineSessionConfig config = BuildConfig();
            var status = AREngineSession.Create(config);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                _errorMessage = $"Failed to create the AR session: {AREngineSession.GetStatusText(status)}";
                Invoke(nameof(DoQuit), 0.5f);
                return;
            }

            _isSessionCreated = true;

            if (AREngineSession.InitialiseCameraTexture() == 0)
            {
                Debug.LogError("arengine: error: failed to initialise the camera texture, " +
                               "the background will stay black");
            }

            AREngineSession.SetDisplayGeometryFromScreen();

            status = AREngineSession.Resume();
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                _errorMessage = $"Failed to resume the AR session: {AREngineSession.GetStatusText(status)}";
                Debug.LogError($"arengine: error: failed to resume: {AREngineSession.GetStatusText(status)}");
                Invoke(nameof(DoQuit), 0.5f);
                return;
            }

            Debug.Log("arengine: session created and resumed");
        }

        private AREngineSessionConfig BuildConfig()
        {
            var config = new AREngineSessionConfig
            {
                arType = arType,
                planeFindingMode = planeFindingMode,
            };

            if (minimalARConfigForVPS)
            {
                // Online localisation needs camera frames and a 6DoF pose, nothing
                // else. Deliberately not touching Main here: reaching for the
                // singleton would create a phantom one whose IsVolumeMeasure default
                // is true, which is exactly what re-armed the heavy configuration
                // in the VPS scenes by mistake.
                config.depthMode = AREngine_ARDepthMode.ARENGINE_DEPTH_MODE_DISABLED;
                config.meshMode = AREngine_ARMeshMode.ARENGINE_MESH_MODE_DISABLED;
                config.semanticMode = AREngine_ARSemanticMode.ARENGINE_SEMANTIC_MODE_NONE;
                config.semanticDenseMode =
                    AREngine_ARSemanticDenseMode.ARENGINE_SEMANTIC_DENSE_MODE_DISABLED;
                config.planeFindingMode = AREngine_ARPlaneFindingMode.ARENGINE_PLANE_FINDING_MODE_DISABLED;
                Debug.Log("arengine: minimal VPS configuration applied " +
                          "(depth, mesh, semantic, dense semantics and plane finding all off)");
                return config;
            }

            config.semanticDenseMode = isVolumeMeasure
                ? AREngine_ARSemanticDenseMode.ARENGINE_SEMANTIC_DENSE_MODE_CUBE_VOLUME
                : AREngine_ARSemanticDenseMode.ARENGINE_SEMANTIC_DENSE_MODE_CUBE_SPACE;

            config.multiFaceMode = isMultiFace
                ? AREngine_ARMultiFaceMode.ARENGINE_MULTIFACE_ENABLE
                : AREngine_ARMultiFaceMode.ARENGINE_MULTIFACE_DISABLE;

            return config;
        }

        private void QuitOnEscape()
        {
            if (Input.GetKey(KeyCode.Escape))
            {
                Invoke(nameof(DoQuit), 0.5f);
            }
        }

        private async void DoQuit()
        {
            await GracefulShutdown();
            Application.Quit();
        }

        /// <summary>
        ///     stops the camera consumers before releasing the native session
        /// </summary>
        /// <returns>a task that completes once the session is destroyed</returns>
        /// <remarks>
        ///     the order is the whole point. Breaking the camera stream first, then
        ///     pausing, then waiting for in-flight HAL buffers to drain, and only then
        ///     stopping and destroying is what keeps the SLAM thread from touching a
        ///     BufferQueue that has already gone away
        /// </remarks>
        public async System.Threading.Tasks.Task GracefulShutdown()
        {
            if (_shutdownDone)
            {
                return;
            }

            _shutdownDone = true;
            try
            {
                MixedSpace.MixedSpaceSDK ms = FindObjectOfType<MixedSpace.MixedSpaceSDK>();
                if (ms != null)
                {
                    ms.enabled = false;
                    if (ms.SpatialSession != null)
                        await ms.SpatialSession.DispatchAsync(MixedSpace.XR.MixedSpaceSessionCommand.Stop);
                    if (ms.Localization != null)
                        await ms.Localization.ShutdownAsync();
                }

                AREnginePlatformSupport support = FindObjectOfType<AREnginePlatformSupport>();
                if (support != null)
                {
                    support.enabled = false;
                    await support.ShutdownAsync();
                }

                AREngineSession.Pause();

                await System.Threading.Tasks.Task.Delay(300);

                AREngineSession.Stop();
                AREngineSession.Destroy();
                _isSessionCreated = false;
                Debug.Log("arengine: shutdown complete, camera consumers stopped before native release");
            }
            catch (Exception e)
            {
                Debug.LogError($"arengine: error: failed to shut down cleanly: {e.Message}");
            }
        }

        private void OnGUI()
        {
            if (_errorMessage == null)
            {
                return;
            }

            GUI.Label(new Rect(0, Screen.height - 100, 200, 200), _errorMessage, _errorStyle);
            if (GUI.Button(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 100, 400, 200), "Quit"))
            {
                DoQuit();
            }
        }

        /// <summary>
        ///     builds the session now, for callers that gate startup behind their own UI
        /// </summary>
        public void ConnectServer()
        {
            if (_initCalled)
            {
                return;
            }

            _initCalled = true;
            ConnectToService();
        }
    }
}
