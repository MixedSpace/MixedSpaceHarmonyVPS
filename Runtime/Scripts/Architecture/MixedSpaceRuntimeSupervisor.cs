using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MixedSpace.XR;
using UnityEngine;

namespace MixedSpace
{
    /// <summary>
    /// Supervises validation, initialization and entitlement refresh for one
    /// composed runtime. It owns asynchronous re-entry and partial-start rules;
    /// the Unity facade only forwards lifecycle intent.
    /// </summary>
    internal sealed class MixedSpaceRuntimeSupervisor
    {
        private readonly MixedSpaceRuntimeComposition m_Composition;
        private readonly MixedSpaceStartupCoordinator m_Startup;
        private readonly Func<MixedSpaceRuntimeControl> m_RuntimeControl;
        private readonly Func<string> m_DeveloperToken;
        private readonly Func<MixedSpaceMapBinding[]> m_DiscoverSceneMaps;
        private readonly Func<MixedSpaceRuntimeSnapshot> m_Snapshot;
        private readonly Action<MixedSpaceRuntimeState> m_ReportState;
        private readonly Action<Exception, string, bool> m_ReportError;
        private readonly Action m_ApplyFrameRate;
        private readonly Action m_InitializationCompleted;
        private readonly Action m_ValidationCompleted;
        private readonly Action m_RuntimeReset;
        private readonly Action m_PublishSnapshot;
        private readonly CancellationToken m_LifecycleToken;
        private readonly SynchronizationContext m_UnityContext;
        private readonly bool m_InitializeAutomatically;
        private readonly MixedSpaceLogger.LoggingLevel m_LoggingLevel;
        private Task m_InitializationTask;
        private LogCallback m_LogCallback;
        private bool m_NativeLogCallbackRegistered;
        private bool m_AuthCacheRefreshAttempted;

        public MixedSpaceRuntimeSupervisor(
            MixedSpaceRuntimeComposition composition,
            MixedSpaceStartupCoordinator startup,
            Func<MixedSpaceRuntimeControl> runtimeControl,
            Func<string> developerToken,
            Func<MixedSpaceMapBinding[]> discoverSceneMaps,
            Func<MixedSpaceRuntimeSnapshot> snapshot,
            Action<MixedSpaceRuntimeState> reportState,
            Action<Exception, string, bool> reportError,
            Action applyFrameRate,
            Action initializationCompleted,
            Action validationCompleted,
            Action runtimeReset,
            Action publishSnapshot,
            CancellationToken lifecycleToken,
            SynchronizationContext unityContext,
            bool initializeAutomatically,
            MixedSpaceLogger.LoggingLevel loggingLevel)
        {
            m_Composition = composition ?? throw new ArgumentNullException(nameof(composition));
            m_Startup = startup ?? throw new ArgumentNullException(nameof(startup));
            m_RuntimeControl = runtimeControl ?? throw new ArgumentNullException(nameof(runtimeControl));
            m_DeveloperToken = developerToken ?? throw new ArgumentNullException(nameof(developerToken));
            m_DiscoverSceneMaps = discoverSceneMaps ?? throw new ArgumentNullException(nameof(discoverSceneMaps));
            m_Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            m_ReportState = reportState ?? throw new ArgumentNullException(nameof(reportState));
            m_ReportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
            m_ApplyFrameRate = applyFrameRate ?? throw new ArgumentNullException(nameof(applyFrameRate));
            m_InitializationCompleted = initializationCompleted;
            m_ValidationCompleted = validationCompleted;
            m_RuntimeReset = runtimeReset;
            m_PublishSnapshot = publishSnapshot;
            m_LifecycleToken = lifecycleToken;
            m_UnityContext = unityContext;
            m_InitializeAutomatically = initializeAutomatically;
            m_LoggingLevel = loggingLevel;
        }

        public bool IsReady { get; private set; }
        public bool? TokenValidationResult { get; private set; }
        // Compatibility values for existing callers; token validity is not a license tier.
        public bool InitializationRunning =>
            m_InitializationTask is { IsCompleted: false };

        public async void BeginValidation()
        {
            MixedSpaceLogger.Level = m_LoggingLevel;
            TokenValidationResult = null;
            m_AuthCacheRefreshAttempted = false;
            m_ReportState(MixedSpaceRuntimeState.Validating);
            try
            {
                string token = m_DeveloperToken();
                await MixedSpaceAuthCache.InstallFromStreamingAssetsAsync(token, m_LifecycleToken);
                m_LifecycleToken.ThrowIfCancellationRequested();
                m_Composition.NetworkEnvironment.ValidateToken(token, DispatchValidationResult);
            }
            catch (OperationCanceledException) when (m_LifecycleToken.IsCancellationRequested) { }
            catch (Exception exception) { m_ReportError(exception, "Validation", true); }
        }

        public Task InitializeAsync()
        {
            if (IsReady)
                return Task.CompletedTask;

            MixedSpaceRuntimeControl control = m_RuntimeControl();
            if (control != null &&
                control.TryGetInFlight(
                    MixedSpaceRuntimeCommand.Rebuild,
                    out Task rebuildTask))
            {
                return InitializeAfterRebuildAsync(rebuildTask);
            }

            if (InitializationRunning)
                return m_InitializationTask;

            m_InitializationTask = InitializeCoreAsync();
            return m_InitializationTask;
        }

        public void MarkUnavailable()
        {
            IsReady = false;
        }

        public void RequestFreshValidationAfterRebuild()
        {
            MixedSpaceCallbackGuard.Invoke(m_RuntimeReset, "RuntimeReset");
            if (!Application.isEditor && m_Composition.PlatformInfo.IsMobile)
                m_Composition.NetworkEnvironment.ConfigureMobilePlatform(true);
            m_InitializationTask = null;
            BeginValidation();
        }

        private async Task InitializeAfterRebuildAsync(Task rebuildTask)
        {
            await rebuildTask;
            await InitializeAsync();
        }

        private async Task InitializeCoreAsync()
        {
            m_ReportState(MixedSpaceRuntimeState.Initializing);
            try
            {
                m_LifecycleToken.ThrowIfCancellationRequested();
                MixedSpaceLogger.Level = m_LoggingLevel;
                MixedSpaceLogger.Log(
                    "Initializing MixedSpace runtime",
                    MixedSpaceLogger.LoggingLevel.Verbose);
                MixedSpaceLogger.Log(
                    $"Runtime platform: {m_Composition.PlatformInfo.Kind} " +
                    $"({m_Composition.PlatformInfo.RuntimeName})",
                    MixedSpaceLogger.LoggingLevel.Verbose);

                EnsureRequiredServices(m_Snapshot());
                await MixedSpaceAuthCache.InstallFromStreamingAssetsAsync(m_DeveloperToken(), m_LifecycleToken);
                m_LifecycleToken.ThrowIfCancellationRequested();
                AttachNativeLogSink();
                ConfigureXrLocServer();
                m_Composition.PrepareHttpTransport();

                string token = m_DeveloperToken();
                if (!string.IsNullOrEmpty(token))
                    PlayerPrefs.SetString("token", token);

                m_ApplyFrameRate();
                IsReady = await m_Startup.StartAsync(
                    m_DiscoverSceneMaps(),
                    m_LifecycleToken);
                m_LifecycleToken.ThrowIfCancellationRequested();

                MixedSpaceLogger.Log($"Runtime initialization completed: IsReady={IsReady}", MixedSpaceLogger.LoggingLevel.Verbose);

                if (!IsReady)
                    throw new MixedSpaceRuntimeUnavailableException(
                        "Runtime service startup did not complete successfully.");

                MixedSpaceLogger.Log(
                    "MixedSpace runtime ready.",
                    MixedSpaceLogger.LoggingLevel.Verbose);
                m_ReportState(MixedSpaceRuntimeState.Ready);
                m_Composition.Runtime?.OnSdkReady();
                MixedSpaceCallbackGuard.Invoke(
                    m_InitializationCompleted,
                    "InitializationComplete");
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException &&
                    m_LifecycleToken.IsCancellationRequested)
                {
                    return;
                }

                IsReady = false;
                m_ReportError(exception, "Initialization", true);
                throw;
            }
        }

        private void DispatchValidationResult(bool isValid)
        {
            if (m_UnityContext != null &&
                !ReferenceEquals(SynchronizationContext.Current, m_UnityContext))
            {
                m_UnityContext.Post(_ => CompleteValidationIfActive(isValid), null);
                return;
            }

            CompleteValidationIfActive(isValid);
        }

        private void CompleteValidationIfActive(bool isValid)
        {
            MixedSpaceLogger.Log(
                $"Token validation completed: isValid={isValid} " +
                $"initializeAutomatically={m_InitializeAutomatically} ", MixedSpaceLogger.LoggingLevel.Verbose);

            if (m_LifecycleToken.IsCancellationRequested)
                return;

            if (!isValid && !m_AuthCacheRefreshAttempted)
            {
                m_AuthCacheRefreshAttempted = true;
                RetryValidationWithBundledCacheAsync();
                return;
            }

            TokenValidationResult = isValid;
            if (!isValid)
            {
                IsReady = false;
                m_ReportError(
                    new InvalidOperationException("[TOKEN_VALIDATION_FAILED] Token 验证失败。请查看授权日志中的原生状态码。"),
                    "Validation",
                    true);
            }

            MixedSpaceCallbackGuard.Invoke(m_ValidationCompleted, "TokenValidationCompleted");
            m_PublishSnapshot?.Invoke();
            if (isValid && m_InitializeAutomatically && !m_LifecycleToken.IsCancellationRequested)
                BeginAutomaticInitialization();
        }

        private async void RetryValidationWithBundledCacheAsync()
        {
            try
            {
                string token = m_DeveloperToken();
                bool refreshed = await MixedSpaceAuthCache.RefreshFromStreamingAssetsAsync(
                    token, m_LifecycleToken);
                m_LifecycleToken.ThrowIfCancellationRequested();
                if (refreshed)
                    m_Composition.NetworkEnvironment.ValidateToken(token, DispatchValidationResult);
                else
                    DispatchValidationResult(false);
            }
            catch (OperationCanceledException) when (m_LifecycleToken.IsCancellationRequested) { }
            catch (Exception exception) { m_ReportError(exception, "Validation", true); }
        }

        private async void BeginAutomaticInitialization()
        {
            try
            {
                await InitializeAsync();
            }
            catch
            {
                // InitializeCoreAsync already published a structured error. Do not
                // rethrow an async-void exception into Unity's context.
            }
        }

        private void AttachNativeLogSink()
        {
            if (m_NativeLogCallbackRegistered)
                return;

            m_LogCallback = MixedSpaceNativeLogBridge.Receive;
            IntPtr callback = Marshal.GetFunctionPointerForDelegate(m_LogCallback);
            m_Composition.NativeCore.Runtime.AttachLogSink(callback);
            m_NativeLogCallbackRegistered = true;
        }

        /// <summary>
        /// One-shot xrloc library configuration at SDK startup, next to the native
        /// log sink. Deliberately not repeated per token verification. The value
        /// comes from the MixedSpaceSDK Inspector (Test = 0, Production = 1).
        /// </summary>
        private void ConfigureXrLocServer()
        {
            int serverStatus = MixedSpaceXrLocNative.SetServer(m_Composition.XrLocServerId);
            if (serverStatus != 0)
                MixedSpaceLogger.LogWarning($"XrLoc server selection rejected: status={serverStatus}");
        }

        private static void EnsureRequiredServices(MixedSpaceRuntimeSnapshot snapshot)
        {
            if (snapshot.HasRequiredComponents)
                return;

            var missing = new List<string>();
            if (!snapshot.HasSpatialSession) missing.Add("SpatialSession");
            if (!snapshot.HasPlatform) missing.Add("Platform");
            if (!snapshot.HasLocalization) missing.Add("Localization");
            if (!snapshot.HasAlignmentSolver) missing.Add("AlignmentSolver");
            if (!snapshot.HasTracking) missing.Add("Tracking");
            throw new MixedSpaceRuntimeUnavailableException(
                $"Required runtime services are unavailable: {string.Join(", ", missing)}.");
        }
    }

    /// <summary>
    /// Raised when the selected platform cannot provide a required runtime
    /// capability. The name describes the failure instead of a task implementation.
    /// </summary>
    public sealed class MixedSpaceRuntimeUnavailableException : Exception
    {
        public MixedSpaceRuntimeUnavailableException(string message) : base(message)
        {
        }
    }
}
