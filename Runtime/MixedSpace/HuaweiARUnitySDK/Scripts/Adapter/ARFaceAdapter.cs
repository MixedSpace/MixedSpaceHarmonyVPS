namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using AREngineBridge;

    // A face, on top of the AREngineBridge.
    //
    // The old layer asked native for the face's pose, geometry and blend shapes
    // through the face handle it had been given. The bridge identifies a face by a
    // session-stable id instead, which is what ARBridge.Id reads out of that handle,
    // and both the geometry and the blend shapes are now taken by id.
    //
    // The pose keeps the quirk it always had. NDKPoseData2UnityPose already puts the
    // face into Unity's left-handed world, and the SDK's face sample wants the Z
    // component back the other way round, so the old adapter negated it a second
    // time after converting. That second negation is not a mistake to clean up: it is
    // what makes the face model line up with the preview once the caller attaches the
    // result to a camera-space object.
    internal class ARFaceAdapter
    {
        private NDKSession m_ndkSession;

        public ARFaceAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        public Pose GetPose(IntPtr faceHandle)
        {
            AREngineFace face;
            if (!AREngineSession.TryGetFaceInfo(ARBridge.Id(faceHandle), out face))
            {
                return Pose.identity;
            }

            Pose result = ARBridge.ToUnityPose(face.centerPose);
            result.position.z = -result.position.z;
            return result;
        }

        public IntPtr AcquireGeometry(IntPtr faceHandle)
        {
            // The geometry adapter snapshots the mesh and hands back a token for it,
            // because ARFaceGeometry holds its handle in a field and releases it from
            // a finalizer while the bridge has one geometry slot per session.
            return m_ndkSession.FaceGeometryAdapter.Acquire(ARBridge.Id(faceHandle));
        }

        public IntPtr AcquireBlendShape(IntPtr faceHandle)
        {
            return m_ndkSession.FaceBlendShapeAdapter.Acquire(ARBridge.Id(faceHandle));
        }
    }
}
