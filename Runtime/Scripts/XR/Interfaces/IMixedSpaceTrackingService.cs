namespace MixedSpace.XR
{
    public interface ITrackingStatus
    {
        int LocalizationAttemptCount { get; }
        int LocalizationSuccessCount { get; }
        int TrackingQuality { get; }
    }

    public interface IMixedSpaceTrackingService
    {
        ITrackingStatus TrackingStatus { get; }

        void Ingest(MixedSpaceTrackingSignal signal);

        void Reset();
    }

    /// <summary>
    /// Aggregate tracking input for one frame. Tracking policy depends on counts,
    /// not provider result objects, which keeps the policy independent of the
    /// localization engine's output representation.
    /// </summary>
    public sealed class MixedSpaceTrackingSignal
    {
        public MixedSpaceTrackingSignal(
            MixedSpaceTrackingSnapshot platform,
            int attemptedProviders,
            int successfulProviders)
        {
            if (attemptedProviders < 0)
                throw new System.ArgumentOutOfRangeException(nameof(attemptedProviders));
            if (successfulProviders < 0 || successfulProviders > attemptedProviders)
                throw new System.ArgumentOutOfRangeException(nameof(successfulProviders));

            Platform = platform;
            AttemptedProviders = attemptedProviders;
            SuccessfulProviders = successfulProviders;
        }

        public MixedSpaceTrackingSnapshot Platform { get; }
        public int AttemptedProviders { get; }
        public int SuccessfulProviders { get; }
    }
}
