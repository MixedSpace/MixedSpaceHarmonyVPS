// Anchors, on top of the AREngineBridge.
//
// The old native layer handed out a reference every time an anchor was created
// or acquired from a list, and ARAnchor/ARAnchorManager released each one when
// they were done with it. The bridge hands out a session-stable id instead and
// keeps the only reference, so releasing an id that the manager still maps
// would free a live anchor and let the bridge hand the same id to the next one.
// That asymmetry is what Release() below has to reproduce, and the manager's
// dictionary is what tells the two release sites apart.

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    internal class ARAnchorAdapter
    {
        private NDKSession m_ndkSession;

        public ARAnchorAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        //anchor
        public Pose GetPose(IntPtr anchorHandle)
        {
            Pose glPose;
            if (!AREngineSession.TryGetAnchorPose(ARBridge.Id(anchorHandle), out glPose))
            {
                return Pose.identity;
            }
            return ARBridge.ToUnityPose(glPose);
        }

        public ARTrackable.TrackingState GetTrackingState(IntPtr anchorHandle)
        {
            int trackingState = (int)(AREngineSession.GetAnchorTrackingState(ARBridge.Id(anchorHandle)));

            if (!ValueLegalityChecker.CheckInt("GetTrackingState", trackingState,
                AdapterConstants.Enum_TrackingState_MinIntValue, AdapterConstants.Enum_TrackingState_MaxIntValue))
            {
                return ARTrackable.TrackingState.STOPPED;
            }
            return (ARTrackable.TrackingState)trackingState;
        }

        public void Detach(IntPtr anchorHandle)
        {
            ARBridge.Ok("ARAnchor.Detach", AREngineSession.DetachAnchor(ARBridge.Id(anchorHandle)));
        }

        public void Release(IntPtr anchorHandle)
        {
            if (m_ndkSession != null && m_ndkSession.AnchorManager.IsTracked(anchorHandle))
            {
                // ARAnchorManager only releases a handle it already has a live
                // ARAnchor for, which used to drop the duplicate reference that
                // the acquire took. The bridge owns the one reference it has.
                return;
            }
            AREngineSession.ReleaseAnchor(ARBridge.Id(anchorHandle));
        }

        //anchor list
        //
        // A list is now a plain managed container: nothing fills one from the
        // native side any more, since the bridge returns anchor batches directly.
        public IntPtr CreateList()
        {
            return BridgeHandleLists.Create();
        }

        public void DestroyList(IntPtr anchorListHandle)
        {
            BridgeHandleLists.Destroy(anchorListHandle);
        }

        public int GetListSize(IntPtr anchorListHandle)
        {
            return BridgeHandleLists.Size(anchorListHandle);
        }

        public IntPtr AcquireListItem(IntPtr anchorListHandle, int index)
        {
            return BridgeHandleLists.ItemAt(anchorListHandle, index);
        }

        public void AddListItem(IntPtr anchorListHandle, IntPtr anchorHandle)
        {
            BridgeHandleLists.Add(anchorListHandle, anchorHandle);
        }
    }
}
