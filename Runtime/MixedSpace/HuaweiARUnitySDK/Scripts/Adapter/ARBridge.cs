// Shared plumbing for the HuaweiARInternal adapter layer now that it drives
// libAREngineBridge.so instead of libHwAREngineUnityApi.so.
//
// Two things had to be reconciled, and this file is where both live so the
// adapters stay readable:
//
//   identity   the old native layer handed out raw pointers for trackables and
//              anchors, and ARTrackable/ARAnchor use that pointer as their
//              Equals/GetHashCode key. The bridge hands out small ints that stay
//              valid for the whole session, so Handle()/Id() translate between the
//              two without giving up "IntPtr.Zero means null".
//
//   handedness the bridge is deliberately a pass-through: every pose, point and
//              vertex it returns is in AREngine's right-handed OpenGL convention.
//              The old SDK converted to Unity's left-handed space in C#, in
//              NDKPoseData, so the adapters still do exactly that conversion here
//              rather than letting the bridge guess at it.

namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal static class ARBridge
    {
        /// <summary>
        ///     turns a bridge id into the handle the SDK passes around
        /// </summary>
        /// <param name="id">the trackableId or anchorId from the bridge</param>
        /// <returns>a non-zero handle, or IntPtr.Zero for an unknown id</returns>
        public static IntPtr Handle(int id)
        {
            // id + 1 rather than id: IntPtr.Zero already means "no trackable" all
            // over the SDK, and id 0 is a perfectly good plane.
            return id < 0 ? IntPtr.Zero : new IntPtr(id + 1);
        }

        /// <summary>
        ///     turns an SDK handle back into the bridge id
        /// </summary>
        /// <param name="handle">the handle a caller handed to an adapter</param>
        /// <returns>the id, or -1 when the handle is null</returns>
        public static int Id(IntPtr handle)
        {
            return handle == IntPtr.Zero ? -1 : handle.ToInt32() - 1;
        }

        /// <summary>
        ///     converts a pose the bridge read out of AREngine into Unity space
        /// </summary>
        /// <param name="glPose">right-handed pose, position in the OpenGL convention</param>
        /// <returns>the same pose in Unity's left-handed world space</returns>
        public static Pose ToUnityPose(Pose glPose)
        {
            var data = new NDKPoseData
            {
                Tx = glPose.position.x,
                Ty = glPose.position.y,
                Tz = glPose.position.z,
                Qx = glPose.rotation.x,
                Qy = glPose.rotation.y,
                Qz = glPose.rotation.z,
                Qw = glPose.rotation.w,
            };
            Pose pose;
            NDKPoseData.NDKPoseData2UnityPose(data, out pose);
            return pose;
        }

        /// <summary>
        ///     converts a Unity pose into the convention AREngine works in
        /// </summary>
        /// <param name="pose">the pose in Unity world space</param>
        /// <returns>the right-handed pose to hand to the bridge</returns>
        public static Pose FromUnityPose(Pose pose)
        {
            var data = new NDKPoseData(pose);
            return new Pose(new Vector3(data.Tx, data.Ty, data.Tz),
                new Quaternion(data.Qx, data.Qy, data.Qz, data.Qw));
        }

        /// <summary>
        ///     reports a bridge call that came back with a status
        /// </summary>
        /// <param name="what">what was being attempted, for the log line</param>
        /// <param name="status">the value AREngine returned</param>
        /// <returns>true when the call succeeded</returns>
        public static bool Ok(string what, AREngineBridge.AREngine_ARStatus status)
        {
            if (status == AREngineBridge.AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return true;
            }
            ARDebug.LogError("{0} failed: {1}", what,
                AREngineBridge.AREngineSession.GetStatusText(status));
            return false;
        }

        /// <summary>
        ///     translates a bridge status into the code the SDK's own adapters use
        /// </summary>
        /// <param name="status">the value AREngine returned</param>
        /// <returns>the closest NDKARStatus, for ARExceptionAdapter.ExtractException</returns>
        /// <remarks>
        ///     the two enums share no values: NDKARStatus numbers its errors from -1
        ///     down, while AREngine reports 201, 401 and a 10092xxxxxx block. Only
        ///     success has to be exact, because that is what ARExceptionAdapter
        ///     tests; what a failure maps to reaches the log as
        ///     "NOT SUCCESS---" plus this name, so prefer the code that says why.
        ///     Ok() has already logged AREngine's own description by then.
        /// </remarks>
        public static NDKARStatus ToNDKStatus(AREngineBridge.AREngine_ARStatus status)
        {
            switch (status)
            {
                case AREngineBridge.AREngine_ARStatus.ARENGINE_SUCCESS:
                    return NDKARStatus.ARENGINE_SUCCESS;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_PERMISSION_NOT_GRANTED:
                    return NDKARStatus.HWAR_ERROR_CAMERA_PERMISSION_NOT_GRANTED;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_INVALID_ARGUMENT:
                    return NDKARStatus.HWAR_ERROR_INVALID_ARGUMENT;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_DEVICE_NOT_SUPPORTED:
                    return NDKARStatus.HWAR_UNAVAILABLE_DEVICE_NOT_COMPATIBLE;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_NOT_SYSTEM_APP:
                    return NDKARStatus.HWAR_UNAVAILABLE_EMUI_NOT_CAPABLE;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_SESSION_PAUSED:
                    return NDKARStatus.HWAR_ERROR_SESSION_PAUSED;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_SESSION_NOT_PAUSED:
                    return NDKARStatus.HWAR_ERROR_SESSION_NOT_PAUSED;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_NOT_TRACKING:
                    return NDKARStatus.HWAR_ERROR_NOT_TRACKING;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_TEXTURE_NOT_SET:
                    return NDKARStatus.HWAR_ERROR_TEXTURE_NOT_SET;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_MISSING_GL_CONTEXT:
                    return NDKARStatus.HWAR_ERROR_MISSING_GL_CONTEXT;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_UNSUPPORTED_CONFIGURATION:
                    return NDKARStatus.HWAR_ERROR_UNSUPPORTED_CONFIGURATION;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_RESOURCE_EXHAUSTED:
                    return NDKARStatus.HWAR_ERROR_RESOURCE_EXHAUSTED;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_NOT_AVAILABLE:
                    return NDKARStatus.HWAR_ERROR_NOT_YET_AVAILABLE;
                case AREngineBridge.AREngine_ARStatus.ARENGINE_ERROR_CAMERA_NOT_AVAILABLE:
                    return NDKARStatus.HWAR_ERROR_CAMERA_NOT_AVAILABLE;
                default:
                    return NDKARStatus.HWAR_ERROR_FATAL;
            }
        }

        /// <summary>
        ///     warns the first time a feature is reached that the native C API does
        ///     not expose
        /// </summary>
        /// <param name="what">the feature name</param>
        /// <remarks>
        ///     AREngine's world-map save/load, anchor serialisation and cloud
        ///     authentication exist only in the ArkTS layer, so these stay loud
        ///     rather than failing silently the way an empty buffer would.
        ///
        ///     It goes to UnityEngine.Debug directly because ARDebug.LogWarning's
        ///     body is commented out in this SDK - routing through it would silence
        ///     the one message that must never be quiet.
        /// </remarks>
        public static void NotSupported(string what)
        {
            if (m_reportedUnsupported.Contains(what))
            {
                return;
            }
            m_reportedUnsupported.Add(what);
            Debug.LogWarning("arengine_unity " + what + " is not available through the AREngine native C API, " +
                "so the AREngineBridge does not expose it.");
        }

        private static readonly HashSet<string> m_reportedUnsupported = new HashSet<string>();
    }
}
