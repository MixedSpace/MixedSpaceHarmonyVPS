using MixedSpace;
using MixedSpace.XR;
using UnityEngine;

namespace AREngineBridge.Glue
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class MixedSpaceHarmonyBootstrap : MixedSpacePlatformBootstrap
    {
        protected override void RegisterServices(MixedSpaceSDK sdk)
        {
#if UNITY_OPENHARMONY && !UNITY_EDITOR
            sdk.SetPlatformInfo(new HarmonyPlatformInfo());
            sdk.SetPoseFusionFactory(new HarmonyPoseFusionFactory());
#endif
        }

        private sealed class HarmonyPlatformInfo : IMixedSpacePlatformInfo
        {
            public MixedSpacePlatformKind Kind => MixedSpacePlatformKind.HarmonyOS;
            public string RuntimeName => Application.platform.ToString();
            public bool IsMobile => true;
        }
    }
}
