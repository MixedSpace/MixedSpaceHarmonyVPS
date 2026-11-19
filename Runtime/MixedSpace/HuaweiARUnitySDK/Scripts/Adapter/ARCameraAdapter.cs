
public struct ARCameraIntrinsics
{
    public float fx;
    public float fy;
    public float cx;
    public float cy;
    public int width;
    public int height;

    public override string ToString()
    {
        return string.Format("fx={0}, fy={1}, cx={2}, cy={3}, size={4}x{5}",
            fx, fy, cx, cy, width, height);
    }
}

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    // The camera, on top of the AREngineBridge.
    //
    // The old layer acquired a native ARCamera from the frame and released it a
    // line later, and every getter passed that pointer back into native code. The
    // bridge reads the camera once per Update and keeps it for the session's
    // lifetime, so what comes back from AcquireCameraHandle is the bridge's own
    // pointer, this class needs it for nothing beyond "is there a camera yet", and
    // Release has become empty for exactly that reason.
    //
    // Poses still go through the right-handed-to-Unity conversion the old C# did,
    // because the bridge reports AREngine's OpenGL convention on purpose. The
    // projection matrix does not: it reaches the caller as AREngine produced it,
    // which is what the old native call did with the same underlying
    // HMS_AREngine_ARCamera_GetProjectionMatrix, and BackGroundRenderer assigns it
    // straight to Camera.projectionMatrix. If the preview ever disagrees with the
    // rendered geometry in depth, that single line is where to look.
    internal class ARCameraAdapter
    {
        public ARCameraAdapter(NDKSession session)
        {
            // The bridge's camera belongs to the session singleton rather than to
            // this NDKSession, so there is no field to keep. The parameter stays
            // because NDKSession constructs every adapter the same way.
        }

        public ARTrackable.TrackingState GetTrackingState(IntPtr cameraHandle)
        {
            if (!HasFrame(cameraHandle))
            {
                return ARTrackable.TrackingState.STOPPED;
            }

            int state = (int)AREngineSession.CurrentFrame.trackingState;
            if (!ValueLegalityChecker.CheckInt("GetTrackingState", state,
                AdapterConstants.Enum_TrackingState_MinIntValue, AdapterConstants.Enum_TrackingState_MaxIntValue))
            {
                return ARTrackable.TrackingState.STOPPED;
            }
            return (ARTrackable.TrackingState)state;
        }

        public Pose GetPose(IntPtr cameraHandle)
        {
            if (!HasFrame(cameraHandle))
            {
                return Pose.identity;
            }

            // cameraPose is the display-oriented one the old
            // HwArCamera_getDisplayOrientedPose returned, already read by Update.
            return ARBridge.ToUnityPose(AREngineSession.CurrentFrame.cameraPose);
        }

        public ARCameraIntrinsics GetImageIntrinsics(IntPtr cameraHandle)
        {
            if (cameraHandle == IntPtr.Zero)
            {
                return default(ARCameraIntrinsics);
            }

            AREngineCameraIntrinsics bridgeIntrinsics;
            if (!AREngineSession.TryGetCameraIntrinsics(out bridgeIntrinsics))
            {
                ARDebug.LogError("GetImageIntrinsics: the bridge reported no intrinsics for this camera");
                return default(ARCameraIntrinsics);
            }

            // Assigned by name, not by memory order. The bridge reads the focal
            // length and the principal point through their own getters, so this
            // cannot pick up the transposition the stale comment on
            // CameraData.Intrinsics keeps confusing people with.
            return new ARCameraIntrinsics
            {
                fx = bridgeIntrinsics.focalLengthX,
                fy = bridgeIntrinsics.focalLengthY,
                cx = bridgeIntrinsics.principalPointX,
                cy = bridgeIntrinsics.principalPointY,
                width = bridgeIntrinsics.imageWidth,
                height = bridgeIntrinsics.imageHeight,
            };
        }

        public Matrix4x4 GetProjectionMatrix(IntPtr cameraHandle, float near, float far)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            if (cameraHandle == IntPtr.Zero)
            {
                return matrix;
            }

            // Reads this near/far pair without moving the session-wide clip planes,
            // and leaves identity behind when there is no camera yet - which is
            // what the old code returned when native wrote nothing.
            AREngineSession.TryGetCameraProjectionMatrix(near, far, out matrix);
            return matrix;
        }

        public void Release(IntPtr cameraHandle)
        {
            // Nothing to release: the bridge owns the camera it read from the
            // frame and drops it with the session.
        }

        /// <summary>
        ///     is there a camera with a frame behind this handle
        /// </summary>
        private static bool HasFrame(IntPtr cameraHandle)
        {
            return cameraHandle != IntPtr.Zero && AREngineSession.CurrentFrame.isValid;
        }
    }
}
