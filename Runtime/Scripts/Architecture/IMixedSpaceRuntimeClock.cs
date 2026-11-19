namespace MixedSpace
{
    /// <summary>
    /// Time source used by runtime services. Keeping Unity time behind this port
    /// makes scheduling deterministic in tests and replaceable on other clients.
    /// </summary>
    public interface IMixedSpaceRuntimeClock
    {
        float Time { get; }
        float UnscaledTime { get; }
        float RealtimeSinceStartup { get; }
    }

    public sealed class UnityMixedSpaceRuntimeClock : IMixedSpaceRuntimeClock
    {
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public float RealtimeSinceStartup => UnityEngine.Time.realtimeSinceStartup;
    }
}
