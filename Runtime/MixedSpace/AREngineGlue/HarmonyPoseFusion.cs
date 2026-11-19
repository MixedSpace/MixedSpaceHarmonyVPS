using MixedSpace;

namespace AREngineBridge.Glue
{
    public sealed class HarmonyPoseFusionFactory : IMixedSpacePoseFusionFactory
    {
        public IMixedSpacePoseFusion Create() => new MixedSpace.XR.MixedSpacePoseFusion();
    }
}
