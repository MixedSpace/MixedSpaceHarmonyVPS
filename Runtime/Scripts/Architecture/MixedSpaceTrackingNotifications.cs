using System;

namespace MixedSpace.XR
{
    /// <summary>
    /// Isolates application callbacks from the tracking model. First-occurrence
    /// flags are committed before invoking user code, so reentrant reset cannot
    /// leave half-updated notification state.
    /// </summary>
    internal sealed class MixedSpaceTrackingNotifications
    {
        private readonly Action m_OnPlatformUnavailable;
        private readonly Action<bool> m_OnStabilized;
        private readonly Action<bool> m_OnDegraded;
        private bool m_IsFirstStabilized = true;
        private bool m_IsFirstDegraded = true;

        public MixedSpaceTrackingNotifications(
            Action onPlatformUnavailable,
            Action<bool> onStabilized,
            Action<bool> onDegraded)
        {
            m_OnPlatformUnavailable = onPlatformUnavailable;
            m_OnStabilized = onStabilized;
            m_OnDegraded = onDegraded;
        }

        public void PlatformUnavailable() =>
            InvokeSafely(m_OnPlatformUnavailable, "PlatformPoseUnavailable");

        public void Publish(MixedSpaceTrackingChange change)
        {
            switch (change)
            {
                case MixedSpaceTrackingChange.None:
                    return;
                case MixedSpaceTrackingChange.Stabilized:
                    bool firstStable = m_IsFirstStabilized;
                    m_IsFirstStabilized = false;
                    InvokeSafely(
                        () => m_OnStabilized?.Invoke(firstStable),
                        "TrackingStabilized");
                    return;
                case MixedSpaceTrackingChange.Degraded:
                    bool firstDegraded = m_IsFirstDegraded;
                    m_IsFirstDegraded = false;
                    InvokeSafely(
                        () => m_OnDegraded?.Invoke(firstDegraded),
                        "TrackingDegraded");
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        public void Clear()
        {
            m_IsFirstStabilized = true;
            m_IsFirstDegraded = true;
        }

        private static void InvokeSafely(Action callback, string eventName)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception exception)
            {
                try
                {
                    MixedSpaceLogger.LogError(
                        $"{eventName} subscriber failed: {exception.Message}");
                }
                catch
                {
                    // Diagnostics must never turn an isolated application-event
                    // failure into tracking-loop failure.
                }
            }
        }
    }
}
