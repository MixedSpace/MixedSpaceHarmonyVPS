using UnityEngine;

namespace MixedSpace
{
    /// <summary>
    /// Operating-system families supported by the platform-neutral SDK layer.
    /// Unknown is intentional: a new platform can boot far enough to report a
    /// clear capability error instead of entering an Android-specific code path.
    /// </summary>
    public enum MixedSpacePlatformKind
    {
        Unknown = 0,
        Android = 1,
        IOS = 2,
        HarmonyOS = 3,
        Windows = 10,
        MacOS = 11,
        Linux = 12,
        Web = 20
    }

    /// <summary>
    /// Provides platform identity without coupling application services to
    /// UnityEngine.Application. Native platform packages may replace this service
    /// before SDK initialization.
    /// </summary>
    public interface IMixedSpacePlatformInfo
    {
        MixedSpacePlatformKind Kind { get; }

        string RuntimeName { get; }

        bool IsMobile { get; }
    }

    /// <summary>
    /// Default Unity implementation used by existing scenes.
    /// </summary>
    public sealed class UnityMixedSpacePlatformInfo : IMixedSpacePlatformInfo
    {
        public MixedSpacePlatformKind Kind => ResolvePlatform(Application.platform);

        public string RuntimeName => Application.platform.ToString();

        public bool IsMobile => Kind == MixedSpacePlatformKind.Android ||
                                Kind == MixedSpacePlatformKind.IOS ||
                                Kind == MixedSpacePlatformKind.HarmonyOS;

        private static MixedSpacePlatformKind ResolvePlatform(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.Android:
                    return MixedSpacePlatformKind.Android;
                case RuntimePlatform.IPhonePlayer:
                    return MixedSpacePlatformKind.IOS;
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return MixedSpacePlatformKind.Windows;
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return MixedSpacePlatformKind.MacOS;
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return MixedSpacePlatformKind.Linux;
                case RuntimePlatform.WebGLPlayer:
                    return MixedSpacePlatformKind.Web;
                default:
                    return MixedSpacePlatformKind.Unknown;
            }
        }
    }
}
