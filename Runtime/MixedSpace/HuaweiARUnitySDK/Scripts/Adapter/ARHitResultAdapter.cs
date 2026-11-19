// Hit results, on top of the AREngineBridge.
//
// The old layer allocated a native hit result per list item and ARHitResult read
// its distance, pose and trackable lazily from that object, so the values had to
// stay reachable until the caller destroyed it. The bridge answers a hit test
// with a batch of complete records, which means the record itself is what has to
// live behind the handle: Store() takes a slot in the table below and every
// getter reads the record it was given, so a hit result the app kept across a
// frame still reports the surface it hit, not whatever the next frame hit.

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using System.Collections.Generic;
    using AREngineBridge;

    internal class ARHitResultAdapter
    {
        private NDKSession m_ndkSession;

        /// <summary>the hit records the SDK currently holds a handle to</summary>
        private readonly BridgeTokenTable<AREngineHit> m_Hits = new BridgeTokenTable<AREngineHit>();

        public ARHitResultAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        /// <summary>
        ///     takes a handle for one hit of the batch AREngineSession.HitTest read
        /// </summary>
        /// <param name="hit">the record the bridge returned</param>
        /// <returns>a handle to give to ARHitResult</returns>
        public IntPtr Store(AREngineHit hit)
        {
            return m_Hits.Store(hit);
        }

        public IntPtr Create()
        {
            // An empty hit is a hit that hit nothing: every getter reads the
            // record behind the handle, and a default record carries no pose, no
            // distance and no trackable, which is what the old empty native
            // object reported before HwArFrame_hitTest wrote into it.
            return m_Hits.Store(default(AREngineHit));
        }

        public void Destroy(IntPtr hitResultHandle)
        {
            m_Hits.Release(hitResultHandle);
        }

        public float GetDistance(IntPtr hitResultHandle)
        {
            AREngineHit hit;
            return m_Hits.TryGet(hitResultHandle, out hit) ? hit.distance : 0f;
        }

        public Pose GetHitPose(IntPtr hitResultHandle)
        {
            AREngineHit hit;
            if (!m_Hits.TryGet(hitResultHandle, out hit))
            {
                return Pose.identity;
            }
            return ARBridge.ToUnityPose(hit.pose);
        }

        //this fuction will not produce more reference
        public ARTrackable AcquireTrackable(IntPtr hitResultHandle)
        {
            return m_ndkSession.TrackableManager.ARTrackableFactory(GetTrackbaleHandle(hitResultHandle), true);
        }

        internal IntPtr GetTrackbaleHandle(IntPtr hitResultHandle)
        {
            AREngineHit hit;
            if (!m_Hits.TryGet(hitResultHandle, out hit))
            {
                return IntPtr.Zero;
            }
            return ARBridge.Handle(hit.trackableId);
        }

        public ARAnchor AcquireNewAnchor(IntPtr hitResultHandle)
        {
            AREngineHit hit;
            if (!m_Hits.TryGet(hitResultHandle, out hit))
            {
                ARDebug.LogError("ARHitResult.CreateAnchor: the hit result is no longer available");
                return null;
            }

            // hit.pose is already in the convention AREngine wants, so it goes to
            // the bridge as it stands rather than through FromUnityPose, which is
            // for poses the app built in Unity space.
            int anchorId = hit.trackableId >= 0
                ? AREngineSession.CreateAnchorOnTrackable(hit.trackableId, hit.pose)
                : AREngineSession.CreateAnchor(hit.pose);
            if (anchorId < 0)
            {
                return null;
            }
            return m_ndkSession.AnchorManager.ARAnchorFactory(ARBridge.Handle(anchorId), true);
        }

        // A hit list is now a plain managed container of hit handles: the frame
        // adapter fills it from the batch the bridge returned and walks it the way
        // the old native list was walked.
        public IntPtr CreateList()
        {
            return BridgeHandleLists.Create();
        }

        public void DestroyList(IntPtr listHandle)
        {
            BridgeHandleLists.Destroy(listHandle);
        }

        public int GetListSize(IntPtr listHandle)
        {
            return BridgeHandleLists.Size(listHandle);
        }

        public IntPtr AcquireListItem(IntPtr listHandle, int index)
        {
            return BridgeHandleLists.ItemAt(listHandle, index);
        }

        public void AddListItem(IntPtr listHandle, IntPtr hitResultHandle)
        {
            BridgeHandleLists.Add(listHandle, hitResultHandle);
        }
    }
}
