using UnityEngine;
using UnityEngine.Events;
using System;
using System.Threading.Tasks;
using MixedSpace.XR;
using Object = UnityEngine.Object;

namespace MixedSpace
{
    public class MixedSpaceSDK : MonoBehaviour
    {
        public const string PackageVersion = "2.0.0";

        [SerializeField] private MixedSpaceRunPlan m_RunPlan;
        [SerializeField, Tooltip("Display on-device logs and error popups, including token validation, localization and pose fusion.")]
        private bool m_ShowRuntimeLogs = true;
        [SerializeField] private MixedSpaceApplicationBinding m_ApplicationBinding;
        [SerializeField] private MixedSpaceScenePorts m_ScenePorts = new MixedSpaceScenePorts();
        private MixedSpaceRuntimeConfiguration m_InstanceSettings;
        private int? m_RuntimeFrameRate;
        public MixedSpaceRunPlan RunPlan => m_RunPlan;
        // Required authoring owners. There is no embedded configuration fallback.
        private MixedSpaceApplicationBinding ApplicationBinding => m_ApplicationBinding != null
            ? m_ApplicationBinding : throw new InvalidOperationException("Assign an Application Binding to MixedSpaceSDK.");

        [SerializeField]
        [Tooltip("SDK 开发者令牌。\nSDK developer token")]
        private string m_DeveloperToken = "";
        [SerializeField] private string m_ServerMapLoad = MixedSpaceNetworkEnvironment.DefaultMapServer;
        [SerializeField] private string m_ServerAddressLocalize = MixedSpaceNetworkEnvironment.DefaultLocalizeServer;
        
        [SerializeField]
        [Tooltip("Production=0、Test=1；选择原生认证环境和默认 HTTP 端点。\nProduction=0, Test=1. Selects native authentication and default HTTP endpoints.")]
        private int m_XrLocServer = (int)MixedSpaceXrLocServer.Production;
        /// <summary>Native xrloc server profile selected on the Inspector.</summary>
        public int XrLocServer => m_XrLocServer;

        /// <summary>
        /// Developer token used by validation and cloud requests.
        /// </summary>
        public string DeveloperToken
        {
            get => m_DeveloperToken;
            set => m_DeveloperToken = value ?? string.Empty;
        }

        #region Interfaced components

        public MixedSpaceRuntimeConfiguration RuntimeConfiguration
        {
            get
            {
                if (m_RunPlan == null)
                    throw new InvalidOperationException("Assign a Run Plan to MixedSpaceSDK.");
                if (m_InstanceSettings != null) return m_InstanceSettings;
                var settings = m_RunPlan.CreateRuntimeSettings();
                settings.CopyScenePortsFrom(m_ScenePorts);
                // Inspector reads are detached; only a running instance caches its policies.
                if (Application.isPlaying)
                {
                    m_InstanceSettings = settings;
                    m_RuntimeFrameRate ??= m_RunPlan.FrameRate;
                }
                return settings;
            }
        }

        public MixedSpaceApplicationSignals ApplicationSignals => ApplicationBinding.Signals;

        public IMixedSpaceSpatialSession SpatialSession => m_Composition?.SpatialSession;

        [SerializeField]
        [MixedSpaceComponentContract(typeof(IMixedSpacePlatformAdapter))]
        private Object m_PlatformAdapter;

        public IMixedSpacePlatformAdapter Platform => m_PlatformAdapter as IMixedSpacePlatformAdapter;

        public IMixedSpaceLocalizationCoordinator Localization =>
            m_Composition?.Localization;

        public IMixedSpaceAlignmentSolver AlignmentSolver => m_Composition?.AlignmentSolver;

        public IMixedSpaceTrackingService Tracking => m_Composition?.Tracking;

        /// <summary>
        /// Runtime-wide recovery boundary. Commands are serialized and duplicate
        /// in-flight requests are coalesced by the supervisor.
        /// </summary>
        public IMixedSpaceRuntimeControl RuntimeControl =>
            m_Coordinator?.RuntimeControl ?? throw new InvalidOperationException(
                "Runtime control is unavailable before the Unity Awake boundary.");

        #endregion Interfaced components

        // Public

        public ITrackingStatus TrackingHealth => Tracking?.TrackingStatus;
        public bool IsOperational => m_Coordinator?.IsReady ?? false;
        public bool? TokenValidationResult => m_Coordinator?.TokenValidationResult;
        public bool HasEntitlementResult => TokenValidationResult.HasValue;
        public MixedSpaceRuntimeState RuntimeState => Diagnostics.State;

        /// <summary>
        /// Map access boundary owned by this SDK instance.
        /// </summary>
        public IMixedSpaceMapRepository Maps
            => Composition.Maps;

        public MixedSpaceMapProvisioner MapProvisioning
            => Composition.MapProvisioning;

        /// <summary>
        /// Platform identity used by application-level decisions. Camera and pose
        /// acquisition remain behind the serialized IMixedSpacePlatformAdapter component.
        /// </summary>
        public IMixedSpacePlatformInfo PlatformInfo => Composition.PlatformInfo;

        /// <summary>
        /// Native localization boundary currently used by the SDK process.
        /// </summary>
        public IMixedSpaceNativeCore NativeCore => Composition.NativeCore;

        /// <summary>
        /// Factory for independent scene pose-fusion sessions.
        /// </summary>
        public IMixedSpacePoseFusionFactory PoseFusionFactory =>
            Composition.PoseFusionFactory;

        /// <summary>
        /// High-level cloud use cases. Applications depend on this boundary rather
        /// than constructing mutable REST job objects.
        /// </summary>
        public IMixedSpaceCloudWorkspace CloudWorkspace =>
            Composition.CloudWorkspace;

        /// <summary>
        /// Token, connectivity and endpoint environment used by the SDK lifecycle.
        /// </summary>
        public IMixedSpaceNetworkEnvironment NetworkEnvironment =>
            Composition.NetworkEnvironment;

        /// <summary>
        /// Replaces the map repository before initialization. This is the intended
        /// extension point for tests and future native platform storage adapters.
        /// Runtime replacement is rejected because registered native maps may still
        /// be owned by the active repository.
        /// </summary>
        /// <param name="repository">Repository to use for this SDK instance.</param>
        /// <exception cref="ArgumentNullException">The repository is null.</exception>
        /// <exception cref="InvalidOperationException">Initialization has started.</exception>
        public void SetMapRepository(IMixedSpaceMapRepository repository) =>
            InstallPortBeforeStart(
                repository,
                nameof(repository),
                "map repository",
                value => Composition.SetMapRepository(value));

        /// <summary>
        /// Assigns platform identity before initialization. HarmonyOS packages use
        /// this hook to identify themselves without modifying the core SDK assembly.
        /// </summary>
        public void SetPlatformInfo(IMixedSpacePlatformInfo platformInfo) =>
            InstallPortBeforeStart(
                platformInfo,
                nameof(platformInfo),
                "platform information",
                value => Composition.SetPlatformInfo(value));

        /// <summary>
        /// Selects the native implementation before initialization. Platform
        /// packages use this hook to install their bridge without changing the core SDK.
        /// </summary>
        public void SetNativeCore(IMixedSpaceNativeCore nativeCore) =>
            InstallPortBeforeStart(
                nativeCore,
                nameof(nativeCore),
                "native core",
                value => Composition.SetNativeCore(value));

        /// <summary>
        /// Selects the platform pose-fusion implementation before initialization.
        /// </summary>
        public void SetPoseFusionFactory(IMixedSpacePoseFusionFactory poseFusionFactory) =>
            InstallPortBeforeStart(
                poseFusionFactory,
                nameof(poseFusionFactory),
                "pose-fusion factory",
                value => Composition.SetPoseFusionFactory(value));

        /// <summary>
        /// Replaces token and endpoint handling before initialization.
        /// </summary>
        public void SetNetworkEnvironment(IMixedSpaceNetworkEnvironment networkEnvironment) =>
            InstallPortBeforeStart(
                networkEnvironment,
                nameof(networkEnvironment),
                "network environment",
                value => Composition.SetNetworkEnvironment(value));

        /// <summary>
        /// Replaces streaming HTTP before initialization. The SDK disposes an
        /// injected transport on destruction when it implements IDisposable.
        /// </summary>
        public void SetHttpTransport(IMixedSpaceHttpTransport httpTransport) =>
            InstallPortBeforeStart(
                httpTransport,
                nameof(httpTransport),
                "HTTP transport",
                value => Composition.SetHttpTransport(value));

        /// <summary>
        /// Replaces the typed cloud client before initialization.
        /// </summary>
        public void SetCloudClient(IMixedSpaceCloudClient cloudClient) =>
            InstallPortBeforeStart(
                cloudClient,
                nameof(cloudClient),
                "cloud client",
                value => Composition.SetCloudClient(value));

        /// <summary>
        /// Raised whenever the runtime lifecycle changes. This C# event is kept
        /// separate from UnityEvents so non-MonoBehaviour clients can subscribe.
        /// </summary>
        public event Action<MixedSpaceRuntimeState> RuntimeStateChanged
        {
            add => Diagnostics.StateChanged += value;
            remove => Diagnostics.StateChanged -= value;
        }

        /// <summary>
        /// Raised when initialization or a critical component operation fails.
        /// </summary>
        public event Action<MixedSpaceRuntimeError> RuntimeErrorOccurred
        {
            add => Diagnostics.ErrorOccurred += value;
            remove => Diagnostics.ErrorOccurred -= value;
        }

        /// <summary>
        /// Raised after lifecycle state changes. The immutable snapshot is suitable
        /// for native UI bridges, telemetry and diagnostics.
        /// </summary>
        public event Action<MixedSpaceRuntimeSnapshot> RuntimeSnapshotChanged
        {
            add => Diagnostics.SnapshotChanged += value;
            remove => Diagnostics.SnapshotChanged -= value;
        }

        public MixedSpaceRuntimeSnapshot GetRuntimeSnapshot()
        {
            return Diagnostics.Capture();
        }

        public int TargetFrameRate
        {
            get { _ = RuntimeConfiguration; return m_RuntimeFrameRate ?? m_RunPlan.FrameRate; }
            set
            {
                _ = RuntimeConfiguration;
                m_RuntimeFrameRate = value;
                ApplyFrameRatePreference();
            }
        }

        public string CacheDirectory
        {
            get { return NetworkEnvironment.DownloadDirectory; }
        }

        private void CloseManualCycleGate()
        {
            if (SpatialSession != null)
            {
                _ = SpatialSession.DispatchAsync(
                    MixedSpaceSessionCommand.BlockLocalization);
            }
        }

        /// <summary>Raised after the runtime has completed staged startup.</summary>
        public UnityEvent RuntimeReady
        {
            get => ApplicationBinding.Ready;
            set => ApplicationBinding.Ready = value;
        }

        /// <summary>Raised when token validation returns; read TokenValidationResult for the result.</summary>
        public UnityEvent EntitlementResolved
        {
            get => ApplicationBinding.Entitlement;
            set => ApplicationBinding.Entitlement = value;
        }

        /// <summary>Raised after a runtime rebuild transaction completes.</summary>
        public UnityEvent RuntimeRebuilt
        {
            get => ApplicationBinding.Rebuilt;
            set => ApplicationBinding.Rebuilt = value;
        }

        private MixedSpaceRuntimeComposition m_Composition;
        private MixedSpaceRuntimeCoordinator m_Coordinator;
        private MixedSpaceRuntimeDiagnostics m_Diagnostics;
        private bool m_IsPrimaryInstance;

        private bool InitializationStarted =>
            IsOperational || (m_Coordinator?.InitializationRunning ?? false);

        private void InstallPortBeforeStart<T>(
            T implementation,
            string parameterName,
            string role,
            Action<T> install) where T : class
        {
            if (implementation == null)
                throw new ArgumentNullException(parameterName);
            if (InitializationStarted)
                throw new InvalidOperationException(
                    $"The {role} must be assigned before MixedSpaceSDK initialization starts.");
            install(implementation);
        }

        private MixedSpaceRuntimeComposition Composition =>
            m_Composition ??= new MixedSpaceRuntimeComposition(
                RuntimeConfiguration,
                ApplicationSignals,
                Platform,
                () => DeveloperToken,
                () => IsOperational,
                CloseManualCycleGate,
                  HandlePlatformTrackingLost,
                  () => m_ServerMapLoad,
                  () => m_ServerAddressLocalize,
                  () => (int)m_XrLocServer);

        private MixedSpaceRuntimeDiagnostics Diagnostics =>
            m_Diagnostics ??= new MixedSpaceRuntimeDiagnostics(
                () => m_Composition?.ExistingMapRepository,
                () => m_Composition?.ExistingPlatformInfo,
                () => IsOperational,
                () => TokenValidationResult,
                () => SpatialSession != null,
                () => Platform != null,
                () => Localization != null,
                () => AlignmentSolver != null,
                () => Tracking != null);

        // Process-native resources still require a single active owner. This is
        // a lifetime guard, not a public service locator for application code.
        private static MixedSpaceSDK s_Current;

        /// <summary>
        /// Activates the pure C# composition and installs application-level
        /// startup/recovery use cases around it.
        /// </summary>
        private void CreateRuntimeCoordinator()
        {
            if (m_Coordinator != null)
                return;

            m_Coordinator = new MixedSpaceRuntimeCoordinator(
                Composition,
                () => DeveloperToken,
                () => FindObjectsOfType<MixedSpaceMapBinding>(),
                GetRuntimeSnapshot,
                SetRuntimeState,
                ReportRuntimeError,
                ApplyFrameRatePreference,
                () => RuntimeReady?.Invoke(),
                () => EntitlementResolved?.Invoke(),
                () => RuntimeRebuilt?.Invoke(),
                PublishRuntimeSnapshot,
                m_RunPlan.StartAutomatically,
                m_RunPlan.Diagnostics);
        }

        private void HandlePlatformTrackingLost()
        {
            MixedSpaceCallbackGuard.Invoke(
                ApplicationSignals.PlatformPoseUnavailable,
                nameof(ApplicationSignals.PlatformPoseUnavailable));
            DispatchRuntimeCommandSafely(MixedSpaceRuntimeCommand.RecoverTracking);
        }

        private void Awake()
        {
            if (s_Current == null)
            {
                s_Current = this;
            }

            if (s_Current != this)
            {
                MixedSpaceLogger.LogError("There must be only one MixedSpaceSDK object in a scene.");
                UnityEngine.Object.DestroyImmediate(this);
                return;
            }

            m_IsPrimaryInstance = true;
            if (m_ShowRuntimeLogs) MixedSpaceLogPanel.EnsureAvailable();
            try
            {
                CreateRuntimeCoordinator();
                m_Coordinator.Begin();
                // Begin invokes the configured validation boundary before automatic initialization.
            }
            catch (Exception e)
            {
                FailRuntime(e, "NetworkEnvironment");
            }
        }

        private void OnEnable()
        {
            // Awake reports missing owners. Do not create a second UI binding path.
            m_ApplicationBinding?.Attach(this);
        }

        private void OnDisable()
        {
            m_ApplicationBinding?.Detach();
        }

        private void Update()
        {
            m_Coordinator?.Tick();
        }

        private void OnApplicationPause(bool paused)
        {
            m_Coordinator?.OnApplicationPause(paused);
        }

        /// <summary>
        /// 初始化 SDK 运行时。并发调用复用同一初始化任务，避免重复加载地图和创建网络客户端。
        /// 不等同于单独开始定位；自动定位由 RunPlan 的 StartWhenReady 配置决定。
        /// </summary>
        public Task StartRuntimeAsync()
        {
            if (m_Coordinator == null)
                return Task.FromException(new InvalidOperationException(
                    "MixedSpace runtime has not reached its Awake boundary."));
            return m_Coordinator.InitializeAsync();
        }

        /// <summary>开始定位或恢复暂停的会话；必须等待 RuntimeReady，不清零统计。可 await，异常交由调用方处理。</summary>
        public Task StartLocalizationAsync() => DispatchLocalizationAsync(MixedSpaceSessionCommand.Begin);

        /// <summary>取消并等待当前定位周期退出，停止后续调度；不卸载地图或关闭相机。必须在 SDK 就绪后调用。</summary>
        public Task StopLocalizationAsync() => DispatchLocalizationAsync(MixedSpaceSessionCommand.Stop);

        /// <summary>重置会话结果和首次成功标志，不清零累计统计；RunPlan.ResumeAfterReinitialize 决定是否恢复定位。</summary>
        public Task ResetLocalizationAsync() => DispatchLocalizationAsync(MixedSpaceSessionCommand.Reset);

        private readonly System.Threading.SemaphoreSlim m_LocalizationCommands = new System.Threading.SemaphoreSlim(1, 1);

        private async Task DispatchLocalizationAsync(MixedSpaceSessionCommand command)
        {
            await m_LocalizationCommands.WaitAsync();
            try
            {
                if (!IsOperational || SpatialSession == null)
                    throw new InvalidOperationException("Wait for RuntimeReady before controlling localization.");
                await SpatialSession.DispatchAsync(command);
            }
            finally { m_LocalizationCommands.Release(); }
        }

        /// <summary>开始定位的按钮/UnityEvent 入口。异步执行，失败通过 RuntimeErrorOccurred 报告；需等待 RuntimeReady。</summary>
        public void StartLocalization() => ExecuteLocalizationCommand(MixedSpaceSessionCommand.Begin);
        /// <summary>停止定位的按钮/UnityEvent 入口。异步执行，不卸载地图或关闭相机；失败通过运行时诊断报告。</summary>
        public void StopLocalization() => ExecuteLocalizationCommand(MixedSpaceSessionCommand.Stop);
        /// <summary>重置定位的按钮/UnityEvent 入口。完成后触发 LocalizationResetCompleted，累计统计不清零。</summary>
        public void ResetLocalization() => ExecuteLocalizationCommand(MixedSpaceSessionCommand.Reset);

        private async void ExecuteLocalizationCommand(MixedSpaceSessionCommand command)
        {
            try { await DispatchLocalizationAsync(command); }
            catch (Exception error) { ReportRuntimeError(error, "LocalizationControl", false); }
        }

        private void ApplyFrameRatePreference()
        {
            Application.targetFrameRate = TargetFrameRate;
        }

        private void FailRuntime(Exception e, string source = "")
        {
            m_Coordinator?.MarkUnavailable();
            ReportRuntimeError(e, source, true);
        }

        private void ReportRuntimeError(Exception e, string component, bool critical)
        {
            Diagnostics.Report(e, component, critical);
        }

        private void SetRuntimeState(MixedSpaceRuntimeState state)
        {
            Diagnostics.TransitionTo(state);
        }

        private void PublishRuntimeSnapshot()
        {
            Diagnostics.PublishSnapshot();
        }

        private void OnDestroy()
        {
            // Destroying a duplicate component must not tear down the primary SDK's
            // maps, HTTP client or process-wide native resources.
            if (!m_IsPrimaryInstance)
                return;

            SetRuntimeState(MixedSpaceRuntimeState.Stopping);
            m_ApplicationBinding?.Detach();
            m_Coordinator?.Dispose();

            SetRuntimeState(MixedSpaceRuntimeState.Stopped);
            m_Coordinator = null;
            m_Composition = null;
            m_Diagnostics = null;
            if (s_Current == this)
                s_Current = null;
        }

        [ContextMenu("Rebuild MixedSpace Runtime")]
        private void RebuildRuntimeFromContextMenu()
        {
            DispatchRuntimeCommandSafely(MixedSpaceRuntimeCommand.Rebuild);
        }

        private async void DispatchRuntimeCommandSafely(MixedSpaceRuntimeCommand command)
        {
            try
            {
                if (m_Coordinator == null)
                    return;
                await RuntimeControl.ExecuteAsync(
                    command,
                    m_Coordinator.LifetimeToken);
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException &&
                    (m_Coordinator == null ||
                     m_Coordinator.IsCancellationRequested))
                    return;

                FailRuntime(e, $"Runtime command {command}");
            }
        }

    }
}
