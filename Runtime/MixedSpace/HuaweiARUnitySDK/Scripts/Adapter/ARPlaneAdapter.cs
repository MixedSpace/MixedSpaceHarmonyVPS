namespace HuaweiARInternal
{
    using System;
    using System.Runtime.InteropServices;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using System.Collections;
    using System.Collections.Generic;
    using AREngineBridge;

    /// <summary>
    ///     plane geometry, classification and containment tests
    /// </summary>
    /// <remarks>
    ///     every entry takes the plane's handle, which is its bridge trackable id,
    ///     so a plane a caller cached in an earlier frame still reads correctly.
    ///     The bridge reports right-handed OpenGL data; the conversions that turn
    ///     that into Unity space live here, in the same places they lived when the
    ///     old native layer did the work
    /// </remarks>
    internal class ARPlaneAdapter
    {
        // Vertex count, not float count: the old layer's limit was 1024 floats,
        // and the bridge hands back Vector2 pairs.
        private const int m_maxPolygonSize = 1024;
        private readonly List<Vector2> m_tmpPolygon = new List<Vector2>();
        NDKSession m_ndkSession;

        public ARPlaneAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        public ARPlane AcquireSubsumedBy(IntPtr planeHandle)
        {
            // -1 when nothing has absorbed this plane, which ARBridge.Handle turns
            // back into IntPtr.Zero so the factory returns null. The subsumer's own
            // id is valid even when C# has never seen that plane in a batch, and the
            // factory is deliberately asked not to create, as before.
            int subsumerId = AREngineSession.GetPlaneSubsumedBy(ARBridge.Id(planeHandle));
            return m_ndkSession.TrackableManager.ARTrackableFactory(ARBridge.Handle(subsumerId), false) as ARPlane;
        }

        public ARPlane.ARPlaneType GetPlaneType(IntPtr planeHandle)
        {
            AREnginePlane plane;
            if (!AREngineSession.TryGetPlaneInfo(ARBridge.Id(planeHandle), out plane))
            {
                return ARPlane.ARPlaneType.UNKNOWN_FACING;
            }
            return (ARPlane.ARPlaneType)plane.planeType;
        }

        public Pose GetCenterPose(IntPtr planeHandle)
        {
            AREnginePlane plane;
            if (!AREngineSession.TryGetPlaneInfo(ARBridge.Id(planeHandle), out plane))
            {
                return Pose.identity;
            }
            return ARBridge.ToUnityPose(plane.centerPose);
        }

        public float GetExtentX(IntPtr planeHandle)
        {
            AREnginePlane plane;
            return AREngineSession.TryGetPlaneInfo(ARBridge.Id(planeHandle), out plane) ? plane.extentX : 0f;
        }

        public float GetExtentZ(IntPtr planeHandle)
        {
            AREnginePlane plane;
            return AREngineSession.TryGetPlaneInfo(ARBridge.Id(planeHandle), out plane) ? plane.extentZ : 0f;
        }

        public void GetPlanePolygon(IntPtr planeHandle, List<Vector3> points)
        {
            points.Clear();
            int written = AREngineSession.GetPlanePolygon(ARBridge.Id(planeHandle), m_tmpPolygon, m_maxPolygonSize);
            if (written >= m_maxPolygonSize)
            {
                ARDebug.LogInfo("GetPlanePolygon: plane polygon size exceeds.");
            }

            // Emitted back to front, with the local Z negated, exactly as the old
            // layer's loop did: the winding has to flip with the handedness or the
            // visualizer's mesh faces away from the camera.
            for (int i = m_tmpPolygon.Count - 1; i >= 0; i--)
            {
                var point = m_tmpPolygon[i];
                var value = new Vector3(point.x, 0, -point.y);
                points.Add(value);
            }
            m_tmpPolygon.Clear();
        }

        public bool IsPoseInExtents(IntPtr planeHandle, Pose pose)
        {
            return AREngineSession.IsPoseInPlaneExtents(ARBridge.Id(planeHandle), ARBridge.FromUnityPose(pose));
        }

        public bool IsPoseInPolygon(IntPtr planeHandle, Pose pose)
        {
            return AREngineSession.IsPoseInPlanePolygon(ARBridge.Id(planeHandle), ARBridge.FromUnityPose(pose));
        }

        public ARPlane.ARPlaneSemanticLabel GetLabel(IntPtr planeHandle)
        {
            AREnginePlane plane;
            if (!AREngineSession.TryGetPlaneInfo(ARBridge.Id(planeHandle), out plane))
            {
                return ARPlane.ARPlaneSemanticLabel.PLANE_OTHER;
            }
            return (ARPlane.ARPlaneSemanticLabel)plane.label;
        }

        // ARENGINE_PLANE_FACING_INVALID, which is what a plane the bridge cannot
        // read reports; the SDK's own enum names the same value.
        private const int plane_type_invalid = 3;
    }
}
