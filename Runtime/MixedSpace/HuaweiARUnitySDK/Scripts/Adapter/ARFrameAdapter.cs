namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using System.Collections;
    using System.Collections.Generic;
    using AREngineBridge;

    internal class ARFrameAdapter
    {
        /// <summary>
        ///     how many records one batch read may return, well past the bridge's
        ///     64-record default: the native lists the old layer walked had no
        ///     caller-set limit
        /// </summary>
        private const int MaxBatchRecords = 512;

        private NDKSession m_ndkSession;

        public ARFrameAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        public IntPtr Create()
        {
            // The bridge creates the frame with the session and updates it in
            // place, so a handle here is that same object: a token the SDK can
            // compare against IntPtr.Zero, not something it owns.
            return AREngineExtended.GetFrame();
        }

        public void Destroy(IntPtr frameHandle)
        {
            // DestroySession releases the frame; NDKSession's guard against a
            // double destroy is what keeps this honest.
        }

        public bool GetDisplayGeometryChanged()
        {
            return AREngineSession.DisplayGeometryChanged;
        }

        public long GetTimestamp()
        {
            return AREngineSession.CurrentFrame.timestampNs;
        }

        public float[] TransformDisplayUvCoords(float[] inUV)
        {
            const int numOfUV = 8;
            float[] tmpIn = new float[numOfUV];
            Array.Copy(inUV, tmpIn, inUV.Length);
            float[] outUV = new float[numOfUV];
            AREngineSession.TransformDisplayUvCoords(tmpIn, outUV);
            return outUV;
        }

        /// <summary>reused by every hit test so a tap does not allocate a result list</summary>
        private readonly List<AREngineHit> m_Hits = new List<AREngineHit>();

        /// <summary>reused by every anchor read, for the same reason</summary>
        private readonly List<AREngineAnchor> m_anchorBatch = new List<AREngineAnchor>();

        /// <summary>and by every trackable read</summary>
        private readonly List<AREngineTrackable> m_trackableBatch = new List<AREngineTrackable>();

        /// <summary>what every anchor looked like the last time updates were asked for</summary>
        private Dictionary<int, AREngineAnchor> m_lastAnchors = new Dictionary<int, AREngineAnchor>();

        /// <summary>the other half of the swap that keeps m_lastAnchors from growing</summary>
        private Dictionary<int, AREngineAnchor> m_updatedAnchors = new Dictionary<int, AREngineAnchor>();

        public void HitTest(float pixelX, float pixelY, List<ARHitResult> hitResultList)
        {
            hitResultList.Clear();

            int cntOfResult = AREngineSession.HitTest(pixelX, pixelY, m_Hits);
            ARDebug.LogInfo("HitTest hitresult {0}", cntOfResult);
            for (int i = 0; i < cntOfResult; i++)
            {
                AREngineHit hit = m_Hits[i];
                IntPtr hitResultHandle = m_ndkSession.HitResultAdapter.Store(hit);
                switch ((NDKARTrackableType)hit.trackableType)
                {
                    case NDKARTrackableType.Plane:
                        hitResultList.Add(new ARHitResult(hitResultHandle, m_ndkSession));
                        break;
                    case NDKARTrackableType.Point:
                        hitResultList.Add(new ARHitResult(hitResultHandle, m_ndkSession));
                        break;
                    case NDKARTrackableType.Invalid:
                        m_ndkSession.HitResultAdapter.Destroy(hitResultHandle);
                        break;
                    case (NDKARTrackableType)0x41520105:
                        m_ndkSession.HitResultAdapter.Destroy(hitResultHandle);
                        break;
                    default:
                        hitResultList.Add(new ARHitResult(hitResultHandle, m_ndkSession));
                        break;
                }
            }
        }

        public void HitTestOnCanMesh(float pixelX, float pixelY, List<ARHitResult> hitResultList)
        {
            hitResultList.Clear();

            int cntOfResult = AREngineSession.HitTest(pixelX, pixelY, m_Hits);
            ARDebug.LogInfo("HitTest hitresult {0}", cntOfResult);
            for (int i = 0; i < cntOfResult; i++)
            {
                hitResultList.Add(new ARHitResult(m_ndkSession.HitResultAdapter.Store(m_Hits[i]), m_ndkSession));
            }
        }

        public void HitTestArea(float[] inPoints, List<ARHitResult> hitResultList)
        {
            hitResultList.Clear();

            if (inPoints == null || inPoints.Length == 0) {
                ARDebug.LogError("HitTestArea inPoints is empty");
                return;
            }

            // The old layer had HwArFrame_hitTestArea, but AREngine's native C API
            // never exposed the area query, so there is nothing for the bridge to
            // call. The old code read the list it had just created, which was
            // always empty, so this returns the same empty result it always did.
            ARBridge.NotSupported("ARFrame.HitTestArea");
        }

        public ARLightEstimate GetLightEstimate()
        {
            return new ARLightEstimate(true, 1.0f);
        }

        public IntPtr AcquirePointCloudHandle()
        {
            // The bridge has no per-frame cloud object to acquire: it reads the
            // cloud of the frame it last updated on demand. What ARPointCloud
            // needs from a handle is only "not released yet", which the point
            // cloud adapter's token table provides, and no cloud at all still
            // comes back as IntPtr.Zero so ARPointCloud keeps throwing.
            return m_ndkSession.PointCloudAdapter.Acquire();
        }

        public IntPtr AcquireCameraHandle()
        {
            // AREngineSession.Update reads the frame's camera and keeps it, so
            // this is the bridge's object rather than a reference the SDK has to
            // release - which is what ARCameraAdapter.Release has become.
            return AREngineExtended.GetCamera();
        }

        public IntPtr AcquireImageMetadata()
        {
            IntPtr imageMetadataHandle = IntPtr.Zero;
            return imageMetadataHandle;
        }

        public void GetUpdatedAnchors(List<ARAnchor> anchorList)
        {
            // AREngine's C API has no ARFrame_getUpdatedAnchors, so the frame's
            // worth of updates is worked out here: read every anchor the bridge
            // holds, and report the ones whose state or pose moved since the last
            // time this was asked.
            anchorList.Clear();
            AREngineSession.GetAnchors(m_anchorBatch);

            for (int i = 0; i < m_anchorBatch.Count; i++)
            {
                AREngineAnchor anchor = m_anchorBatch[i];
                AREngineAnchor previous;
                bool hasPrevious = m_lastAnchors.TryGetValue(anchor.anchorId, out previous);
                m_updatedAnchors[anchor.anchorId] = anchor;

                if (hasPrevious && previous.trackingState == anchor.trackingState &&
                    previous.pose.position == anchor.pose.position &&
                    previous.pose.rotation == anchor.pose.rotation)
                {
                    continue;
                }

                IntPtr anchorHandle = ARBridge.Handle(anchor.anchorId);
                anchorList.Add(m_ndkSession.AnchorManager.ARAnchorFactory(anchorHandle, false));
            }

            // Swapping rather than clearing the old snapshot is what drops the
            // anchors the SDK has since released, so the table cannot grow past
            // the live anchor set.
            Dictionary<int, AREngineAnchor> done = m_lastAnchors;
            m_lastAnchors = m_updatedAnchors;
            m_updatedAnchors = done;
            m_updatedAnchors.Clear();
        }

        public void GetUpdatedTrackables(List<ARTrackable> trackableList)
        {
            trackableList.Clear();
            int count = AREngineSession.GetUpdatedTrackables(AREngine_ARTrackableType.ARENGINE_TRACKABLE_BASE,
                m_trackableBatch, MaxBatchRecords);
            for (int i = 0; i < count; i++)
            {
                IntPtr trackableHandle = ARBridge.Handle(m_trackableBatch[i].trackableId);
                trackableList.Add(m_ndkSession.TrackableManager.ARTrackableFactory(trackableHandle, false));
            }
        }

        public IntPtr AcquireCameraImage()
        {
            // The bridge keeps one image for the frame it last updated, so the
            // handle is the image adapter's to hand out. A frame with no image
            // still comes back as IntPtr.Zero, which is what HWSupport tests;
            // the old ExtractException threw at that same point and its caller
            // caught it, so failing quietly is the narrower behaviour of the two.
            return m_ndkSession.ImageAdapter.Acquire();
        }

        public IntPtr AcquireDepthImage()
        {
            IntPtr imageHandle = IntPtr.Zero;
            return imageHandle;
        }

        public ARWorldMappingState GetMappingState()
        {
            int ret = (int)ARWorldMappingState.NOT_AVAILABLE;
            return (ARWorldMappingState)ret;
        }

        public IntPtr AcquireSceneMesh()
        {
            // The scene mesh adapter reads the bridge's mesh into managed arrays and
            // hands back a token for that snapshot, because ARSceneMesh holds the
            // handle in a field, releases it from a finalizer, and the bridge has one
            // mesh slot that the next acquire overwrites.
            return m_ndkSession.SceneMeshAdapter.Acquire();
        }

    }
}
