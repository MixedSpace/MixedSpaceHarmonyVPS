namespace HuaweiARUnitySDK
{
    using HuaweiARInternal;
    using System;
    using UnityEngine;

    public class ARAugmentedImage : ARTrackable
    {

        internal ARAugmentedImage(IntPtr trackableHandle, NDKSession session) : base(trackableHandle, session)
        {

        }

        public Pose GetCenterPose()
        {
            return m_ndkSession.AugmentedImageAdapter.GetCenterPose(m_trackableHandle);
        }


        public float GetExtentX()
        {
            return m_ndkSession.AugmentedImageAdapter.GetExtentX(m_trackableHandle);
        }
        public float GetExtentZ()
        {
            return m_ndkSession.AugmentedImageAdapter.GetExtentZ(m_trackableHandle);
        }

        public int GetDataBaseIndex()
        {
            return m_ndkSession.AugmentedImageAdapter.GetDataBaseIndex(m_trackableHandle);
        }

        public string AcquireName()
        {
            return m_ndkSession.AugmentedImageAdapter.AcquireName(m_trackableHandle);
        }
    }
}
///@endcond