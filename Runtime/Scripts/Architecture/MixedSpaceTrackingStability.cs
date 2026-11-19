using System;

namespace MixedSpace.XR
{
    internal enum MixedSpaceTrackingChange
    {
        None,
        Stabilized,
        Degraded
    }

    /// <summary>
    /// Temporal state machine for localization confidence. It knows neither
    /// provider results nor Unity events. Successful observations raise the score
    /// to at most three; silent retention intervals lower it one step per advance.
    /// </summary>
    internal sealed class MixedSpaceTrackingStability
    {
        private const int StableScore = 3;
        private readonly float m_RetentionSeconds;
        private float m_LastObservationTime;
        private bool m_HasPose;
        private bool m_IsStable;

        public MixedSpaceTrackingStability(float retentionSeconds)
        {
            if (float.IsNaN(retentionSeconds) ||
                float.IsInfinity(retentionSeconds) ||
                retentionSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retentionSeconds),
                    "Tracking retention must be finite and non-negative.");
            }
            m_RetentionSeconds = retentionSeconds;
        }

        public int Score { get; private set; }

        public MixedSpaceTrackingChange Advance(
            bool hasAnyAttempt,
            int successfulObservations,
            float now)
        {
            if (successfulObservations < 0)
                throw new ArgumentOutOfRangeException(nameof(successfulObservations));
            if (float.IsNaN(now) || float.IsInfinity(now))
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!hasAnyAttempt)
                return MixedSpaceTrackingChange.None;

            if (successfulObservations > 0)
            {
                m_LastObservationTime = now;
                Score = successfulObservations >= StableScore - Score
                    ? StableScore
                    : Score + successfulObservations;
                m_HasPose = true;
            }
            else if (now - m_LastObservationTime > m_RetentionSeconds)
            {
                m_LastObservationTime = now;
                Score = Math.Max(Score - 1, 0);
            }

            if (m_HasPose && Score < 1)
            {
                m_HasPose = false;
                if (m_IsStable)
                {
                    m_IsStable = false;
                    return MixedSpaceTrackingChange.Degraded;
                }
            }
            else if (m_HasPose &&
                     successfulObservations > 0 &&
                     Score >= StableScore &&
                     !m_IsStable)
            {
                m_IsStable = true;
                return MixedSpaceTrackingChange.Stabilized;
            }

            return MixedSpaceTrackingChange.None;
        }

        public void Clear()
        {
            Score = 0;
            m_LastObservationTime = 0f;
            m_HasPose = false;
            m_IsStable = false;
        }
    }

    /// <summary>
    /// Detects only the positive-to-zero platform-quality edge. Starting at zero
    /// is not a loss; quality must first become available, preserving SDK behavior.
    /// </summary>
    internal sealed class MixedSpacePlatformAvailability
    {
        private bool m_WasAvailable;

        public bool Observe(int quality)
        {
            if (quality > 0)
            {
                m_WasAvailable = true;
                return false;
            }

            // Only the documented zero value represents loss. Preserve the
            // previous behavior for undefined negative platform qualities.
            if (quality != 0)
                return false;
            if (!m_WasAvailable)
                return false;
            m_WasAvailable = false;
            return true;
        }

        public void Clear() => m_WasAvailable = false;
    }
}
