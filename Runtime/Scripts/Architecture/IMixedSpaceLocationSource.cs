using System;

namespace MixedSpace
{
    /// <summary>
    /// Platform-neutral geographic sample used by localization features.
    /// </summary>
    public readonly struct MixedSpaceLocationSample
    {
        public MixedSpaceLocationSample(
            double latitude,
            double longitude,
            double altitude,
            double horizontalAccuracy,
            double verticalAccuracy)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
            HorizontalAccuracy = horizontalAccuracy;
            VerticalAccuracy = verticalAccuracy;
        }

        public double Latitude { get; }
        public double Longitude { get; }
        public double Altitude { get; }
        public double HorizontalAccuracy { get; }
        public double VerticalAccuracy { get; }
    }

    /// <summary>
    /// Port implemented by each supported operating system. Runtime localization
    /// depends on this contract rather than Java, Objective-C or Harmony APIs.
    /// </summary>
    public interface IMixedSpaceLocationSource : IDisposable
    {
        bool IsAvailable { get; }
        bool IsServiceEnabled { get; }
        bool TryStart();
        bool TryRead(out MixedSpaceLocationSample sample);
        void Stop();
    }
}
