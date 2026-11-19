// The feature point cloud, on top of the AREngineBridge.
//
// AREngine owns one cloud per frame and the bridge reads it straight out of the
// frame it last updated, so there is no native cloud object to hold a pointer
// to. What the SDK surface still needs is a handle that means "this cloud has
// not been released yet", because ARPointCloud throws once its handle is zeroed
// and PointcloudVisualizer acquires and releases one every frame - hence the
// token table, which says whether a handle is still live without pretending
// there is a per-cloud object behind it.
//
// The points themselves come back in AREngine's right-handed world space, so the
// Z negation the old layer's callers relied on is applied here.

namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using AREngineBridge;

    internal class ARPointCloudAdapter
    {
        /// <summary>which handles still refer to a live cloud</summary>
        private readonly BridgeTokenTable<bool> m_Clouds = new BridgeTokenTable<bool>();

        /// <summary>reused by every read so a frame does not allocate the cloud twice</summary>
        private readonly List<Vector4> m_Points = new List<Vector4>();

        public ARPointCloudAdapter(NDKSession session)
        {
            // The session is not needed any more - the bridge reads the cloud of
            // the frame it last updated - but NDKSession builds every adapter the
            // same way, so the signature stays.
        }

        /// <summary>
        ///     takes a handle for the cloud of the frame that was just updated
        /// </summary>
        /// <returns>the handle, or IntPtr.Zero when this frame has no cloud</returns>
        /// <remarks>
        ///     ARFrame.AcquirePointCloud() hands the result to ARPointCloud, which
        ///     treats a zero handle as "not yet available" and throws, so reporting
        ///     an empty cloud as no cloud at all keeps that contract.
        /// </remarks>
        public IntPtr Acquire()
        {
            return AREngineSession.PointCloudCount > 0 ? m_Clouds.Store(true) : IntPtr.Zero;
        }

        public long GetTimestamp(IntPtr pointCloudHandle)
        {
            return IsLive(pointCloudHandle) ? AREngineSession.PointCloudTimestamp : 0L;
        }

        public int GetNumberOfPoints(IntPtr pointCloudHandle)
        {
            return IsLive(pointCloudHandle) ? AREngineSession.PointCloudCount : 0;
        }

        public Vector4 GetPoint(IntPtr pointCloudHandle, int index)
        {
            // The bridge copies the cloud as a batch, so a single point still costs
            // a read of it. ARPointCloud does not expose this and no sample calls it.
            CopyPointCloud(pointCloudHandle);
            if (index < 0 || index >= m_Points.Count)
            {
                return Vector4.zero;
            }
            Vector4 point = m_Points[index];
            return new Vector4(point.x, point.y, -point.z, point.w);
        }

        public void CopyPoints(IntPtr pointCloudHandle, List<Vector4> points)
        {
            points.Clear();
            CopyPointCloud(pointCloudHandle);
            for (int i = 0; i < m_Points.Count; ++i)
            {
                Vector4 point = m_Points[i];
                points.Add(new Vector4(point.x, point.y, -point.z, point.w));
            }
        }

        public void CopyPoints(IntPtr pointCloudHandle, List<Vector3> points)
        {
            points.Clear();
            CopyPointCloud(pointCloudHandle);
            for (int i = 0; i < m_Points.Count; ++i)
            {
                Vector4 point = m_Points[i];
                points.Add(new Vector3(point.x, point.y, -point.z));
            }
        }

        public void Release(IntPtr pointCloudHandle)
        {
            m_Clouds.Release(pointCloudHandle);
        }

        private void CopyPointCloud(IntPtr pointCloudHandle)
        {
            m_Points.Clear();
            if (!IsLive(pointCloudHandle))
            {
                return;
            }
            // A handle stays live until it is released, so the frame it was
            // acquired from may already be gone and may have been followed by one
            // with no cloud at all.
            int count = AREngineSession.PointCloudCount;
            if (count > 0)
            {
                AREngineSession.GetPointCloud(m_Points, count);
            }
        }

        private bool IsLive(IntPtr pointCloudHandle)
        {
            bool live;
            return pointCloudHandle != IntPtr.Zero && m_Clouds.TryGet(pointCloudHandle, out live) && live;
        }
    }
}
