// Feature points tracked by AREngine.
//
// A point is just a trackable, so the whole adapter is two by-id reads. The
// bridge returns the pose in AREngine's right-handed convention, which is what
// the old native layer handed to NDKPoseData before converting it, so the
// conversion still happens here through ARBridge.ToUnityPose.

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    class ARPointAdapter
    {
        public ARPointAdapter(NDKSession session)
        {
            // Both reads are by bridge id, so the session handle is not needed
            // any more - but NDKSession builds every adapter the same way, so the
            // signature stays.
        }

        public Pose GetPose(IntPtr pointHandle)
        {
            Pose glPose;
            if (!AREngineSession.TryGetTrackablePose(ARBridge.Id(pointHandle), out glPose))
            {
                return Pose.identity;
            }
            return ARBridge.ToUnityPose(glPose);
        }

        public ARPoint.OrientationMode GetOrientationMode(IntPtr pointHandle)
        {
            AREngine_ARPointOrientationMode mode;
            if (!AREngineSession.TryGetPointOrientationMode(ARBridge.Id(pointHandle), out mode))
            {
                return ARPoint.OrientationMode.INITIALIZED_TO_IDENTITY;
            }
            return (ARPoint.OrientationMode)mode;
        }
    }
}
