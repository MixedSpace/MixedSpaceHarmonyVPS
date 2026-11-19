using System;

namespace MixedSpace.XR
{
    /// <summary>
    /// Value snapshot produced by the tracking ledger. Attempt accounting is
    /// independent from stability decay and notification delivery.
    /// </summary>
    internal readonly struct MixedSpaceTrackingTotals
    {
        public MixedSpaceTrackingTotals(int attempts, int successes)
        {
            Attempts = attempts;
            Successes = successes;
        }

        public int Attempts { get; }
        public int Successes { get; }
    }

    /// <summary>
    /// Accumulates localization outcomes and exposes successful observations
    /// exactly once to the stability model. Counters saturate instead of wrapping
    /// after a long-running installation reaches Int32.MaxValue.
    /// </summary>
    internal sealed class MixedSpaceTrackingLedger
    {
        private readonly bool m_CountProvidersSeparately;
        private int m_Attempts;
        private int m_Successes;
        private int m_PendingSuccesses;

        public MixedSpaceTrackingLedger(bool countProvidersSeparately)
        {
            m_CountProvidersSeparately = countProvidersSeparately;
        }

        public MixedSpaceTrackingTotals Totals =>
            new MixedSpaceTrackingTotals(m_Attempts, m_Successes);

        public bool HasAttempt => m_Attempts > 0;
        public int PendingSuccessfulObservations => m_PendingSuccesses;

        public void Record(MixedSpaceTrackingSignal signal)
        {
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));

            int attemptIncrement = m_CountProvidersSeparately
                ? signal.AttemptedProviders
                : signal.AttemptedProviders > 0 ? 1 : 0;
            int successIncrement = m_CountProvidersSeparately
                ? signal.SuccessfulProviders
                : signal.SuccessfulProviders > 0 ? 1 : 0;

            m_Attempts = SaturatingAdd(m_Attempts, attemptIncrement);
            m_Successes = SaturatingAdd(m_Successes, successIncrement);
            m_PendingSuccesses = SaturatingAdd(m_PendingSuccesses, successIncrement);
        }

        public void AcknowledgeSuccessfulObservations()
        {
            m_PendingSuccesses = 0;
        }

        public void Clear()
        {
            m_Attempts = 0;
            m_Successes = 0;
            m_PendingSuccesses = 0;
        }

        private static int SaturatingAdd(int current, int increment)
        {
            if (increment <= 0)
                return current;
            return current > int.MaxValue - increment
                ? int.MaxValue
                : current + increment;
        }
    }

    /// <summary>
    /// Stable read-only view used by public API consumers. The object identity is
    /// preserved across reset for applications that cache ITrackingStatus, while
    /// only the policy can publish new values into it.
    /// </summary>
    internal sealed class MixedSpaceTrackingStatusView : ITrackingStatus
    {
        public int LocalizationAttemptCount { get; private set; }
        public int LocalizationSuccessCount { get; private set; }
        public int TrackingQuality { get; private set; }

        public void Publish(MixedSpaceTrackingTotals totals, int quality)
        {
            LocalizationAttemptCount = totals.Attempts;
            LocalizationSuccessCount = totals.Successes;
            TrackingQuality = quality;
        }
    }
}
