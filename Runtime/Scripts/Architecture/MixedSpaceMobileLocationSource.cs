namespace MixedSpace
{
    /// <summary>HarmonyOS location source; the Editor has no device location provider.</summary>
    public sealed class MixedSpaceMobileLocationSource : IMixedSpaceLocationSource
    {
#if UNITY_OPENHARMONY && !UNITY_EDITOR
        private readonly MixedSpaceHarmonyLocationSource m_Harmony = new MixedSpaceHarmonyLocationSource();

        public bool IsAvailable => m_Harmony.IsAvailable;
        public bool IsServiceEnabled => m_Harmony.IsServiceEnabled;
        public bool TryStart() => m_Harmony.TryStart();
        public bool TryRead(out MixedSpaceLocationSample sample) => m_Harmony.TryRead(out sample);
        public void Stop() => m_Harmony.Stop();
        public void Dispose() => m_Harmony.Dispose();
#else
        public bool IsAvailable => false;
        public bool IsServiceEnabled => false;
        public bool TryStart() => false;
        public bool TryRead(out MixedSpaceLocationSample sample)
        {
            sample = default;
            return false;
        }
        public void Stop() { }
        public void Dispose() { }
#endif
    }
}
