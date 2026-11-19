namespace HuaweiARInternal
{
    using System;
    using System.Linq;
    using HuaweiARUnitySDK;
    internal static class AdapterConstants
    {

        // No native library name any more: every adapter reaches AREngine through
        // libAREngineBridge.so, whose imports are declared in the AREngineBridge
        // assembly, so this class holds only the tracking-state bounds below.     
        

        public static int Enum_TrackingState_MaxIntValue = Enum.GetValues(typeof(ARTrackable.TrackingState))
            .Cast<int>().Max();
        public static int Enum_TrackingState_MinIntValue = Enum.GetValues(typeof(ARTrackable.TrackingState))
            .Cast<int>().Min();
     
    }
}
