using System;
using System.Threading;
using System.Threading.Tasks;
using MixedSpace.REST;

namespace MixedSpace.XR
{
    /// <summary>
    /// One Cloud-mode provider, with two mutually exclusive solve paths. Keep the
    /// Cloud ID so existing map routing and application callbacks remain stable.
    /// The localization coordinator serializes plan changes, frame evaluation and
    /// release. A frame is never solved concurrently by both backends.
    /// </summary>
    internal sealed class MixedSpaceCloudWithOfflineFallback : IMixedSpaceLocalizationProvider
    {
        // Keep this switch available for cloud-only diagnostics.
        private const float RetryCloudAfterSeconds = 30f;
        private const float CloudRequestTimeoutSeconds = 12f;

        /// <summary>
        /// Consecutive cloud misses that earn one native solve as a second opinion.
        /// A cloud miss is a real answer rather than a connectivity failure, so it
        /// must not divert the pipeline on its own. It only becomes suspicious when
        /// it repeats while a native map is resident, because the two paths are not
        /// equivalent: the upload is a lossy encoding of the same frame, the server
        /// can rate-limit, and it may hold a different revision of the map. One
        /// native solve per this many misses keeps the extra cost bounded, and a hit
        /// parks cloud for the usual retry window instead of re-uploading every frame.
        /// Zero disables the cross-check.
        /// </summary>
        private const int CloudMissesBeforeOfflineCrossCheck = 3;

        private readonly IMixedSpaceLocalizationProvider m_Cloud;
        private readonly IMixedSpaceLocalizationProvider m_Offline;
        private readonly IMixedSpaceOfflineMapReadiness m_Maps;
        private readonly Func<bool> m_HasNetwork;
        private readonly IMixedSpaceRuntimeClock m_Clock;
        private float m_RetryCloudAt;
        private bool m_UsingOffline;
        private bool m_ReportedMissingMaps;
        private int m_ConsecutiveCloudMisses;

        public MixedSpaceCloudWithOfflineFallback(IMixedSpaceLocalizationProvider cloud,
            IMixedSpaceLocalizationProvider offline, IMixedSpaceOfflineMapReadiness maps,
            Func<bool> hasNetwork, IMixedSpaceRuntimeClock clock)
        {
            m_Cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            m_Offline = offline ?? throw new ArgumentNullException(nameof(offline));
            // Custom repositories must opt into readiness; never assume a cloud
            // map ID means that native binary data is loaded.
            m_Maps = maps;
            m_HasNetwork = hasNetwork ?? throw new ArgumentNullException(nameof(hasNetwork));
            m_Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public string ProviderId => MixedSpaceProviderIds.Cloud;
        public MixedSpaceProviderActivationMode ActivationMode => m_Cloud.ActivationMode;

        public async Task<bool> ApplyAsync(MixedSpaceProviderPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            bool active = await m_Cloud.ApplyAsync(plan);
            // Cloud solver parameters are not local solver parameters. Preserve
            // RunPlan.OnDevice settings; only mirror map additions/removals.
            await m_Offline.ApplyAsync(plan);
            m_ReportedMissingMaps = false;
            m_ConsecutiveCloudMisses = 0;
            return active;
        }

        public async Task<MixedSpaceLocalizationOutcome> EvaluateAsync(IMixedSpaceCameraCapture capture,
            CancellationToken cancellationToken)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            cancellationToken.ThrowIfCancellationRequested();
            bool connected = m_HasNetwork();
            if (!connected || m_Clock.RealtimeSinceStartup < m_RetryCloudAt)
                return await SolveOffline(capture, cancellationToken);

            try
            {
                var attempt = await SolveCloudWithConnectivity(capture, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt.Disconnected)
                {
                    m_RetryCloudAt = m_Clock.RealtimeSinceStartup + RetryCloudAfterSeconds;
                    return await SolveOffline(capture, cancellationToken);
                }
                // A normal server miss still proves the cloud request completed.
                // Do not reinterpret it as a connectivity failure.
                m_RetryCloudAt = 0;

                if (attempt.Outcome != null && attempt.Outcome.Success)
                {
                    m_ConsecutiveCloudMisses = 0;
                    ReportCloudRestored();
                    return attempt.Outcome;
                }

                var native = await CrossCheckOfflineAfterMisses(capture, cancellationToken);
                if (native != null)
                    return native;
                ReportCloudRestored();
                return attempt.Outcome;
            }
            catch (MixedSpaceCloudRequestException error) when
                (error.Kind == MixedSpaceCloudFailureKind.Transport || error.Kind == MixedSpaceCloudFailureKind.Timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Reachability can stay true when Wi-Fi has no internet. Avoid
                // repeating a full network timeout before every offline attempt.
                m_RetryCloudAt = m_Clock.RealtimeSinceStartup + RetryCloudAfterSeconds;
                return await SolveOffline(capture, cancellationToken);
            }
        }

        /// <summary>
        /// Counts repeat cloud misses and, at the threshold, asks the native solver
        /// once about the very same frame. Returns the native outcome only when it
        /// localizes; otherwise it returns null and the caller keeps the cloud miss,
        /// so a miss stays a miss unless a resident map proves otherwise.
        /// </summary>
        private async Task<MixedSpaceLocalizationOutcome> CrossCheckOfflineAfterMisses(
            IMixedSpaceCameraCapture capture, CancellationToken cancellationToken)
        {
            if (CloudMissesBeforeOfflineCrossCheck <= 0 || m_Maps?.HasOfflineMaps != true)
                return null;
            if (++m_ConsecutiveCloudMisses < CloudMissesBeforeOfflineCrossCheck)
                return null;

            m_ConsecutiveCloudMisses = 0;
            var native = await SolveOffline(capture, cancellationToken);
            if (native == null || !native.Success)
            {
                // Keep the log honest: the attempt above may have flipped the flag.
                m_UsingOffline = false;
                return null;
            }

            // The server could not match a frame the native solver can, so stay
            // native for the retry window instead of uploading every frame.
            m_RetryCloudAt = m_Clock.RealtimeSinceStartup + RetryCloudAfterSeconds;
            MixedSpaceLogger.Log(
                $"Cloud missed {CloudMissesBeforeOfflineCrossCheck} frames in a row; offline map solving answered, cloud paused for {RetryCloudAfterSeconds:F0}s.");
            return native;
        }

        /// <summary>
        /// Announces the return to cloud once, and only when cloud is in fact the
        /// path that produced the result being returned.
        /// </summary>
        private void ReportCloudRestored()
        {
            if (!m_UsingOffline)
                return;
            m_UsingOffline = false;
            MixedSpaceLogger.Log("Cloud localization restored after offline fallback.");
        }

        /// <summary>
        /// Detect loss of connectivity even while an upload is in flight. Cancel
        /// and await the cloud task before reusing the capture for native solving;
        /// never abandon an encoder/upload task that still owns a pixel lease.
        /// The awaited loop resumes on Unity's context for the reachability read.
        /// </summary>
        private async Task<(bool Disconnected, MixedSpaceLocalizationOutcome Outcome)> SolveCloudWithConnectivity(
            IMixedSpaceCameraCapture capture, CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var pending = m_Cloud.EvaluateAsync(capture, linked.Token);
                bool disconnected = false;
                bool timedOut = false;
                float deadline = m_Clock.RealtimeSinceStartup + CloudRequestTimeoutSeconds;
                while (!pending.IsCompleted)
                {
                    await Task.WhenAny(pending, Task.Delay(250, linked.Token));
                    if (pending.IsCompleted) break;
                    if (cancellationToken.IsCancellationRequested) break;
                    if (m_Clock.RealtimeSinceStartup >= deadline)
                    {
                        timedOut = true;
                        linked.Cancel();
                        break;
                    }
                    if (!m_HasNetwork())
                    {
                        disconnected = true;
                        linked.Cancel();
                        break;
                    }
                }
                try { return (false, await pending); }
                catch (OperationCanceledException) when ((disconnected || timedOut) && !cancellationToken.IsCancellationRequested)
                {
                    if (timedOut)
                        MixedSpaceLogger.LogWarning($"Cloud localization timed out after {CloudRequestTimeoutSeconds:F0}s.");
                    return (true, null);
                }
            }
        }

        private async Task<MixedSpaceLocalizationOutcome> SolveOffline(IMixedSpaceCameraCapture capture,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_Maps?.HasOfflineMaps != true)
            {
                if (!m_ReportedMissingMaps)
                {
                    m_ReportedMissingMaps = true;
                    MixedSpaceLogger.LogWarning("Network unavailable; offline localization cannot start because no native map is ready. Embed map data or load maps while online first.");
                }
                return MixedSpaceLocalizationOutcome.Missed();
            }
            m_ReportedMissingMaps = false;
            if (!m_UsingOffline)
            {
                m_UsingOffline = true;
                MixedSpaceLogger.Log("Cloud localization switched to offline map solving.");
            }
            return await m_Offline.EvaluateAsync(capture, cancellationToken);
        }

        public async Task ReleaseAsync()
        {
            try { await m_Cloud.ReleaseAsync(); }
            finally
            {
                await m_Offline.ReleaseAsync();
                m_RetryCloudAt = 0;
                m_UsingOffline = false;
                m_ReportedMissingMaps = false;
                m_ConsecutiveCloudMisses = 0;
            }
        }
    }
}
