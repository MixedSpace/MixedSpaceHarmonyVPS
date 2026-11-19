namespace HuaweiARInternal
{
    using System;
    using System.Runtime.InteropServices;
    using HuaweiARUnitySDK;
    using UnityEngine;
    using System.Collections.Generic;
    using AREngineBridge;

    /// <summary>
    ///     trackable identity, tracking state and anchor attachment
    /// </summary>
    /// <remarks>
    ///     a trackable handle is a bridge trackable id offset by one, see
    ///     <see cref="ARBridge.Handle"/>, and the ids stay valid for the whole
    ///     session, so an ARTrackable façade keeps working after its frame ends
    /// </remarks>
    internal class ARTrackbaleAdapter
    {
        private NDKSession m_ndkSession;

        // GetAnchors is not on the per-frame hot path, but it is called once per
        // trackable a visualizer walks, so keep one scratch list rather than
        // allocating a new one every call.
        private readonly List<int> m_anchorIds = new List<int>();

        public ARTrackbaleAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        public NDKARTrackableType GetType(IntPtr trackableHandle)
        {
            AREngineTrackable trackable;
            if (!AREngineSession.TryGetTrackableInfo(ARBridge.Id(trackableHandle), out trackable))
            {
                return NDKARTrackableType.BaseTrackable;
            }
            return (NDKARTrackableType)trackable.trackableType;
        }

        public ARTrackable.TrackingState GetTrackingState(IntPtr trackableHandle)
        {
            AREngineTrackable trackable;
            if (!AREngineSession.TryGetTrackableInfo(ARBridge.Id(trackableHandle), out trackable))
            {
                return ARTrackable.TrackingState.STOPPED;
            }
            // Both enums order TRACKING, PAUSED, STOPPED the same way, so the
            // values carry straight over.
            return (ARTrackable.TrackingState)trackable.trackingState;
        }

        public bool AcquireNewAnchor(IntPtr trackableHandle, Pose pose, out IntPtr anchorHandle)
        {
            anchorHandle = IntPtr.Zero;
            // The bridge logs a failed create itself, so there is no status to
            // report back beyond "no anchor".
            int anchorId = AREngineSession.CreateAnchorOnTrackable(ARBridge.Id(trackableHandle),
                ARBridge.FromUnityPose(pose));
            if (anchorId < 0)
            {
                return false;
            }
            anchorHandle = ARBridge.Handle(anchorId);
            return true;
        }

        public void GetAnchors(IntPtr trackableHandle, List<ARAnchor> anchors)
        {
            AREngineSession.GetTrackableAnchorIds(ARBridge.Id(trackableHandle), m_anchorIds);

            anchors.Clear();
            for (int i = 0; i < m_anchorIds.Count; i++)
            {
                IntPtr anchorHandle = ARBridge.Handle(m_anchorIds[i]);

                ARAnchor anchor = m_ndkSession.AnchorManager.ARAnchorFactory(anchorHandle, false);

                if (anchorHandle == IntPtr.Zero)
                {
                    Debug.LogFormat("anchor {0} is not valid", anchorHandle);
                }
                else
                {
                    anchors.Add(anchor);
                }
            }
            m_anchorIds.Clear();
        }

        /// <summary>
        ///     kept for signature compatibility; releases nothing
        /// </summary>
        /// <remarks>
        ///     the bridge owns the AREngine reference behind an id and drops it only
        ///     at TJAREX_DestroySession, which is what lets ids stay valid across
        ///     frames. Releasing per façade would hand back an id another cached
        ///     ARTrackable is still using, so there is deliberately nothing to do here
        /// </remarks>
        public void Release(IntPtr trackableHandle)
        {
        }

        //trackable list

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

        public void AddListItem(IntPtr listHandle, IntPtr trackableHandle)
        {
            BridgeHandleLists.Add(listHandle, trackableHandle);
        }
    }
}
