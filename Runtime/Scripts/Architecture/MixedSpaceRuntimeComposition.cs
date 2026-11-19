using System;
using System.Collections.Generic;
using System.Net.Http;
using MixedSpace.REST;
using MixedSpace.XR;

namespace MixedSpace
{
    /// <summary>
    /// Composition root and lifetime owner for one MixedSpace runtime. Defaults,
    /// injected ports, object-graph construction and owned transport cleanup live
    /// here so the Unity component is only a scene/application boundary.
    /// </summary>
    internal sealed class MixedSpaceRuntimeComposition : IDisposable
    {
        private readonly MixedSpaceRuntimeConfiguration m_Configuration;
        private readonly MixedSpaceApplicationSignals m_Signals;
        private readonly IMixedSpacePlatformAdapter m_Platform;
        private readonly Func<string> m_DeveloperToken;
        private readonly Func<bool> m_IsSdkReady;
        private readonly Action m_CloseManualLocalizationGate;
        private readonly Action m_HandleTrackingLoss;
        private readonly Func<int> m_XrLocServerId;

        /// <summary>
        /// Preloads XrLoc map data in Cloud mode for on-device fallback.
        /// Retired map containers are rejected before native import.
        /// </summary>
        private const bool PrepareCloudOfflineMaps = true;

        private IMixedSpaceMapRepository m_MapRepository;
        private bool m_MapRepositoryIsInjected;
        private MixedSpaceMapProvisioner m_MapProvisioner;
        private IMixedSpaceNativeCore m_NativeCore;
        private IMixedSpacePoseFusionFactory m_PoseFusionFactory;
        private IMixedSpaceCloudClient m_CloudClient;
        private bool m_CloudClientIsInjected;
        private IMixedSpaceCloudContext m_CloudContext;
        private IMixedSpaceCloudWorkspace m_CloudWorkspace;
        private IMixedSpaceMapContentSource m_MapContentSource;
        private MixedSpaceMapPreviewService m_MapPreviewService;
        private IMixedSpacePlatformInfo m_PlatformInfo;
        private IMixedSpaceNetworkEnvironment m_NetworkEnvironment;
        private IMixedSpaceHttpTransport m_HttpTransport;
        private HttpClient m_HttpClient;
        private MixedSpaceRuntime m_Runtime;
        private MixedSpaceLocalizationEngine m_ComposingLocalization;
        private MixedSpaceWorldRegistry m_WorldRegistry;
        private bool m_Disposed;

        public MixedSpaceRuntimeComposition(
            MixedSpaceRuntimeConfiguration configuration,
            MixedSpaceApplicationSignals signals,
            IMixedSpacePlatformAdapter platform,
            Func<string> developerToken,
            Func<bool> isSdkReady,
            Action closeManualLocalizationGate,
            Action handleTrackingLoss,
            Func<string> mapServer = null,
            Func<string> localizeServer = null,
            Func<int> xrLocServerId = null)
        {
            m_Configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            m_Signals = signals ?? throw new ArgumentNullException(nameof(signals));
            m_Platform = platform;
            m_DeveloperToken = developerToken ??
                throw new ArgumentNullException(nameof(developerToken));
            m_IsSdkReady = isSdkReady ?? throw new ArgumentNullException(nameof(isSdkReady));
            m_CloseManualLocalizationGate = closeManualLocalizationGate;
            m_HandleTrackingLoss = handleTrackingLoss;
            m_XrLocServerId = xrLocServerId;
            // Server delegates are passed through, so Inspector edits reach the
            // network layer on every request without rebuilding the composition.
            m_NetworkEnvironment = new MixedSpaceNetworkEnvironment(mapServer, localizeServer, xrLocServerId);
        }

        /// <summary>Inspector-selected xrloc server profile, read live per use.</summary>
        public int XrLocServerId => m_XrLocServerId != null
            ? m_XrLocServerId()
            : (int)MixedSpaceXrLocServer.Production;

        public MixedSpaceRuntime Runtime => m_Runtime;
        // Diagnostics must not construct services while reporting startup failures.
        internal IMixedSpaceMapRepository ExistingMapRepository => m_MapRepository;
        internal IMixedSpacePlatformInfo ExistingPlatformInfo => m_PlatformInfo;
        public IMixedSpacePlatformAdapter Platform => m_Platform;
        public IMixedSpaceSpatialSession SpatialSession => m_Runtime?.SpatialSession;
        public IMixedSpaceLocalizationCoordinator Localization =>
            m_Runtime?.Localization ?? m_ComposingLocalization;
        public IMixedSpaceAlignmentSolver AlignmentSolver => m_Runtime?.AlignmentSolver;
        public IMixedSpaceTrackingService Tracking => m_Runtime?.Tracking;
        public MixedSpaceWorldRegistry Worlds => m_WorldRegistry;

        public IMixedSpaceMapRepository Maps
        {
            get
            {
                ThrowIfDisposed();
                if (m_MapRepository != null)
                    return m_MapRepository;
                if (Localization == null)
                    Compose();
                return m_MapRepository ??= new MixedSpaceMapCatalog(
                    NativeCore,
                    MapContentSource,
                    m_Configuration.LocalizationMode,
                    prepareOfflineMaps: PrepareCloudOfflineMaps,
                    canDownload: HasNetworkConnection);
            }
        }

        public MixedSpaceMapProvisioner MapProvisioning
        {
            get
            {
                ThrowIfDisposed();
                if (m_MapProvisioner == null)
                    Compose();
                return m_MapProvisioner;
            }
        }

        public IMixedSpacePlatformInfo PlatformInfo =>
            m_PlatformInfo ??= new UnityMixedSpacePlatformInfo();

        public IMixedSpaceNativeCore NativeCore =>
            m_NativeCore ??= CreateNativeCore();

        public IMixedSpacePoseFusionFactory PoseFusionFactory =>
            m_PoseFusionFactory ??= new MixedSpacePoseFusionFactory();

        public IMixedSpaceCloudWorkspace CloudWorkspace =>
            m_CloudWorkspace ??= new MixedSpaceCloudWorkspace(CloudContext);

        public IMixedSpaceNetworkEnvironment NetworkEnvironment =>
            m_NetworkEnvironment ??= new MixedSpaceNetworkEnvironment();

        public MixedSpaceMapPreviewService MapPreviews =>
            m_MapPreviewService ??= new MixedSpaceMapPreviewService(MapContentSource);

        private IMixedSpaceCloudClient CloudClient =>
            m_CloudClient ??= new MixedSpaceCloudClient(NetworkEnvironment, HttpTransport);

        private IMixedSpaceCloudContext CloudContext =>
            m_CloudContext ??= new MixedSpaceCloudContext(
                () => CloudClient,
                () => NetworkEnvironment,
                m_DeveloperToken);

        private IMixedSpaceMapContentSource MapContentSource =>
            m_MapContentSource ??= new MixedSpaceCloudMapContentSource(CloudContext);

        /// <summary>
        /// Builds the XrLoc core for the current Editor and HarmonyOS integration.
        /// </summary>
        private IMixedSpaceNativeCore CreateNativeCore()
        {
            string priorLogPath = m_Configuration.WritePoseLogs
                ? MixedSpaceLocalizationJournal.GetPositionPriorLogPath()
                : null;
            var core = new MixedSpaceXrLocCore(priorLogPath);
            MixedSpaceLogger.Log($"xrloc version={MixedSpaceXrLocCore.DescribeVersion() ?? "unavailable"}");
            return core;
        }

        private IMixedSpaceHttpTransport HttpTransport
        {
            get
            {
                if (m_HttpTransport == null)
                {
                    if (m_HttpClient == null)
                        throw new InvalidOperationException(
                            "HTTP transport is not available before SDK initialization.");
                    m_HttpTransport = new SystemMixedSpaceHttpTransport(m_HttpClient);
                }

                return m_HttpTransport;
            }
        }

        // Sample live connectivity on Unity's thread. TokenNet's startup probe is
        // not a network-change monitor; transport failures cover unreachable servers.
        private static bool HasNetworkConnection() =>
            UnityEngine.Application.internetReachability != UnityEngine.NetworkReachability.NotReachable;

        public void Compose()
        {
            ThrowIfDisposed();
            if (m_Runtime != null)
                return;
            if (m_Platform == null)
                throw new InvalidOperationException(
                    "MixedSpaceSDK requires one IMixedSpacePlatformAdapter component.");

            var clock = new UnityMixedSpaceRuntimeClock();
            if (m_Platform is IMixedSpaceCaptureDiagnostics captureDiagnostics)
                captureDiagnostics.SetWritePoseLogs(m_Configuration.WritePoseLogs);
            m_WorldRegistry = new MixedSpaceWorldRegistry(m_Platform, PoseFusionFactory,
                m_Configuration.WritePoseLogs, m_Configuration.PoseFusionConfig);
            var localization = new MixedSpaceLocalizationEngine();
            var eventAdapter = new MixedSpaceUnityEventAdapter(
                m_Signals,
                m_CloseManualLocalizationGate);

            // Map storage needs a coordinator while built-in providers need map
            // storage. This explicit two-phase construction is the only place the
            // cycle exists; provider installation seals the graph afterwards.
            m_ComposingLocalization = localization;
            IMixedSpaceMapRepository maps = Maps;
            // Register one selected policy. Cloud's internal offline fallback is
            // not a second engine provider, so a frame contributes only one result.
            // Activate providers when maps are added.
            var providers = new List<IMixedSpaceLocalizationProvider>();
            if (m_Configuration.LocalizationMode == MixedSpaceLocalizationMode.Cloud)
            {
                var options = MixedSpaceRuntimeConfiguration.CopyValue(m_Configuration.Cloud);
                options.ActivationMode = MixedSpaceProviderActivationMode.WhenNecessary;
                var cloud = new MixedSpaceCloudLocalization(
                    options,
                    maps,
                    NativeCore,
                    clock,
                    CloudContext,
                    progress => MixedSpaceCallbackGuard.Invoke(
                        () => m_Signals.CloudSolveProgress?.Invoke(progress),
                        nameof(m_Signals.CloudSolveProgress)));
                var localOptions = new MixedSpaceOnDeviceOptions
                {
                    ActivationMode = MixedSpaceProviderActivationMode.WhenNecessary,
                    SolverType = options.SolverType,
                    PriorNearestNeighborCount = options.PriorNearestNeighborCountMin,
                    PriorRadius = options.PriorRadius
                };
                var offline = new MixedSpaceOnDeviceLocalization(localOptions, maps, NativeCore, clock);
                providers.Add(new MixedSpaceCloudWithOfflineFallback(cloud, offline,
                    maps as IMixedSpaceOfflineMapReadiness, HasNetworkConnection, clock));
            }
            else
            {
                var options = MixedSpaceRuntimeConfiguration.CopyValue(m_Configuration.OnDevice);
                options.ActivationMode = MixedSpaceProviderActivationMode.WhenNecessary;
                providers.Add(new MixedSpaceOnDeviceLocalization(options, maps, NativeCore, clock));
            }
            providers.AddRange(m_Configuration.ExtensionProviders);
            BindProviderServices(providers, maps);
            localization.InstallProviders(providers);

            m_MapProvisioner = new MixedSpaceMapProvisioner(
                maps,
                localization,
                m_WorldRegistry,
                MapPreviews);
            var tracking = new MixedSpaceTrackingPolicy(
                m_Configuration.Tracking,
                clock,
                m_HandleTrackingLoss,
                isFirst => MixedSpaceCallbackGuard.Invoke(
                    () => m_Signals.TrackingStabilized?.Invoke(isFirst),
                    nameof(m_Signals.TrackingStabilized)),
                isFirst => MixedSpaceCallbackGuard.Invoke(
                    () => m_Signals.TrackingDegraded?.Invoke(isFirst),
                    nameof(m_Signals.TrackingDegraded)));
            IMixedSpaceAlignmentSolver alignmentSolver =
                m_Configuration.AlignmentSolverOverride ??
                new MixedSpaceAlignmentSolver();

            IMixedSpaceLocalizationBinding[] views = m_Configuration.LocalizationViews;
            foreach (IMixedSpaceLocalizationBinding view in views)
            {
                if (view is IMixedSpaceNativeCoreConsumer nativeCoreConsumer)
                    nativeCoreConsumer.SetNativeCore(NativeCore);
            }

            var session = new MixedSpaceSessionController(
                m_Configuration.Session,
                clock,
                m_IsSdkReady,
                m_Platform,
                localization,
                maps,
                alignmentSolver,
                tracking,
                views,
                eventAdapter,
                eventAdapter, m_Configuration.WritePoseLogs);
            m_Runtime = new MixedSpaceRuntime(
                session,
                localization,
                alignmentSolver,
                tracking,
                m_WorldRegistry);
            m_ComposingLocalization = null;
        }

        public void PrepareHttpTransport()
        {
            ThrowIfDisposed();
            if (m_HttpClient == null && m_HttpTransport == null)
            {
                var handler = new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Automatic
                };
                m_HttpClient = new HttpClient(handler);
                m_HttpClient.DefaultRequestHeaders.ExpectContinue = false;
                m_HttpClient.Timeout = TimeSpan.FromDays(1);
            }

            _ = HttpTransport;
        }

        public void SetMapRepository(IMixedSpaceMapRepository repository)
        {
            m_MapRepository = repository ?? throw new ArgumentNullException(nameof(repository));
            m_MapRepositoryIsInjected = true;
        }

        public void SetPlatformInfo(IMixedSpacePlatformInfo platformInfo)
        {
            m_PlatformInfo = platformInfo ??
                throw new ArgumentNullException(nameof(platformInfo));
        }

        public void SetNativeCore(IMixedSpaceNativeCore nativeCore)
        {
            m_NativeCore = nativeCore ?? throw new ArgumentNullException(nameof(nativeCore));
            if (!m_MapRepositoryIsInjected)
                m_MapRepository = null;
        }

        public void SetPoseFusionFactory(IMixedSpacePoseFusionFactory factory)
        {
            m_PoseFusionFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public void SetNetworkEnvironment(IMixedSpaceNetworkEnvironment environment)
        {
            m_NetworkEnvironment = environment ??
                throw new ArgumentNullException(nameof(environment));
            if (!m_CloudClientIsInjected)
                m_CloudClient = null;
        }

        public void SetHttpTransport(IMixedSpaceHttpTransport transport)
        {
            m_HttpTransport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!m_CloudClientIsInjected)
                m_CloudClient = null;
        }

        public void SetCloudClient(IMixedSpaceCloudClient client)
        {
            m_CloudClient = client ?? throw new ArgumentNullException(nameof(client));
            m_CloudClientIsInjected = true;
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;

            m_Runtime?.Dispose();
            m_Runtime = null;
            m_ComposingLocalization = null;
            m_MapProvisioner = null;
            if (m_MapRepository != null)
            {
                try
                {
                    m_MapRepository.ReleaseNativeState();
                }
                catch (Exception exception)
                {
                    MixedSpaceLogger.LogError(
                        $"Map repository cleanup failed: {exception.Message}");
                }
            }

            try
            {
                if (m_HttpTransport is IDisposable disposableTransport)
                    disposableTransport.Dispose();
            }
            catch (Exception exception)
            {
                MixedSpaceLogger.LogError(
                    $"HTTP transport cleanup failed: {exception.Message}");
            }

            m_HttpTransport = null;
            m_HttpClient?.Dispose();
            m_HttpClient = null;
            m_CloudClient = null;
            m_MapPreviewService = null;
            m_MapContentSource = null;
            m_CloudContext = null;
            m_CloudWorkspace = null;
            m_WorldRegistry = null;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(MixedSpaceRuntimeComposition));
        }

        private void BindProviderServices(
            IEnumerable<IMixedSpaceLocalizationProvider> providers,
            IMixedSpaceMapRepository maps)
        {
            foreach (IMixedSpaceLocalizationProvider provider in providers)
            {
                if (provider is IMixedSpaceMapRepositoryConsumer repositoryConsumer)
                    repositoryConsumer.SetMapRepository(maps);
                if (provider is IMixedSpaceNativeCoreConsumer nativeCoreConsumer)
                    nativeCoreConsumer.SetNativeCore(NativeCore);
            }
        }
    }
}
