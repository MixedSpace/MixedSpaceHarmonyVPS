namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using HuaweiARUnitySDK;

    internal class ARAnchorManager
    {
        private  Dictionary<IntPtr, ARAnchor> m_anchorDict =
            new Dictionary<IntPtr, ARAnchor>(new IntPtrComparer());

        private  NDKSession m_ndkSession;

        public ARAnchorManager(NDKSession session)
        {
            m_ndkSession = session;
        }

        public  ARAnchor ARAnchorFactory(IntPtr nativeHandle, bool isCreate=true)
        {
            if (nativeHandle == IntPtr.Zero)
            {
                return null;
            }
            ARAnchor result;
            if (m_anchorDict.TryGetValue(nativeHandle, out result))
            {
                m_ndkSession.AnchorAdapter.Release(nativeHandle);
                return result;
            }
            if (isCreate)
            {
                ARAnchor anchor = new ARAnchor(nativeHandle,m_ndkSession);
                m_anchorDict.Add(nativeHandle, anchor);
                return anchor;
            }
            return null;
        }

        public void RemoveAnchor(ARAnchor anchor)
        {
            m_anchorDict.Remove(anchor.m_anchorHandle);
        }

        /// <summary>
        ///     whether the manager still holds an ARAnchor for this handle.
        ///     ARAnchorAdapter.Release() uses it to tell a duplicate acquire from
        ///     the release that means the anchor is gone for good.
        /// </summary>
        public bool IsTracked(IntPtr anchorHandle)
        {
            return m_anchorDict.ContainsKey(anchorHandle);
        }

        public void GetAllAnchor(List<ARAnchor> anchors)
        {
            anchors.Clear();
            foreach(ARAnchor anchor in m_anchorDict.Values)
            {
                anchors.Add(anchor);
            }
        }
        
    }
}
