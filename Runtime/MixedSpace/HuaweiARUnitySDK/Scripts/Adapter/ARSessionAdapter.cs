// Session lifecycle, on top of the AREngineBridge.
//
// The old layer owned one native AREngine session per NDKSession and passed its
// pointer to every call. The bridge is a singleton around exactly that session -
// AREngine itself only supports one - so this adapter drives AREngineSession and
// hands back the bridge's real session pointer wherever the SDK still expects a
// handle. The pointer is only ever a token now: nothing P/Invokes with it.
//
// One ordering note matters. ARSessionManager creates the NDKSession first and
// configures it afterwards, so AREngineSession.Create has to apply something
// before the app's own config arrives. It gets world tracking with the focus
// locked, because an unlocked autofocus is enough to stop VIO from ever
// entering tracking on a session that was resumed without a config.

namespace HuaweiARInternal
{
    using UnityEngine;
    using System;
    using HuaweiARUnitySDK;
    using System.Collections.Generic;
    using AREngineBridge;

    internal class ARSessionAdapter
    {
        /// <summary>
        ///     how many trackables one batch read may return
        /// </summary>
        /// <remarks>
        ///     the native list the old layer walked had no cap the caller set, so
        ///     this is only the bridge's record buffer being asked for more than
        ///     its 64-record default
        /// </remarks>
        private const int MaxBatchRecords = 512;

        private NDKSession m_ndkSession;

        /// <summary>scratch batches, so a query per frame does not allocate</summary>
        private readonly List<AREngineAnchor> m_anchorBatch = new List<AREngineAnchor>();
        private readonly List<AREngineTrackable> m_trackableBatch = new List<AREngineTrackable>();

        public ARSessionAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        public IntPtr Create()
        {
            ARDebug.LogInfo("bridge create seesion begin");

            AREngine_ARStatus status = AREngineSession.Create(new AREngineSessionConfig
            {
                focusMode = AREngine_ARFocusMode.ARENGINE_FOCUS_MODE_FIXED,
            });
            ARDebug.LogInfo("bridge create seesion returns status {0}", status);
            IntPtr sessionHandle = AREngineExtended.GetSession();
            if (sessionHandle == IntPtr.Zero)
            {
                ARExceptionAdapter.ExtractException(ARBridge.ToNDKStatus(status));
            }
            return sessionHandle;
        }

        public void Destroy()
        {
            ARDebug.LogInfo("bridge destroy session begin, handle =0x{0}",
                m_ndkSession.SessionHandle.ToString("x8"));
            AREngineSession.Destroy();
            ARDebug.LogInfo("bridge destroy session end");
        }

        public bool IsSupported(ARConfigBase unityConfig)
        {
            // HwArSession_checkSupported never existed on this path either, and the
            // AREngine C API has no counterpart: capability probing is per feature
            // and lives in the ArkTS layer. AREngineSession.IsSupported can answer
            // for a single feature, which is as much as this ever claimed.
            return true;
        }

        public void Config(ARConfigBase unityConfig)
        {
            IntPtr configHandle = m_ndkSession.ConfigBaseAdapter.Create();
            if (configHandle == IntPtr.Zero)
            {
                ARDebug.LogError("bridge config skipped: AREngine refused to begin a configuration");
                return;
            }
            m_ndkSession.ConfigBaseAdapter.UpdateNDKConfigWithUnityConfig(configHandle, unityConfig);
            NDKARStatus status = ARBridge.ToNDKStatus(AREngineExtended.ConfigCommit());
            m_ndkSession.ConfigHandle = configHandle;
            ARDebug.LogInfo("bridge config end with value:{0}", status);
        }

        public void Resume()
        {
            ARExceptionAdapter.ExtractException(ARBridge.ToNDKStatus(AREngineSession.Resume()));
        }

        public void Pause()
        {
            AREngine_ARStatus status = AREngineSession.Pause();
            ARDebug.LogInfo("bridge pause end with value:{0}", status);
            ARExceptionAdapter.ExtractException(ARBridge.ToNDKStatus(status));
        }

        public void Stop()
        {
            AREngine_ARStatus status = AREngineSession.Stop();
            ARDebug.LogInfo("bridge stop end with value:{0}", status);
            ARExceptionAdapter.ExtractException(ARBridge.ToNDKStatus(status));
        }

        public void Update()
        {
            GL.InvalidateState();
            // The bridge advances AREngine on the render thread, because that is
            // the only thread with the engine's GL context current, and waits for
            // it so the pose and the camera texture describe the same instant.
            AREngineSession.Update();
            GL.InvalidateState();
        }

        public void SetCameraTextureName(int textureId)
        {
            ARBridge.Ok("ARSession.SetCameraTextureName",
                AREngineExtended.SetCameraGLTexture((uint)textureId));
        }

        public void SetDisplayGeometry(ScreenOrientation orientation, int width, int height)
        {
            AREngine_ARPoseType harmonyOrientation = AREngineSession.RotationForOrientation(orientation);
            ARDebug.LogInfo("bridge set display geometry begin with harmony oritentation:{0}, width={1}, height={2}",
                harmonyOrientation, width, height);
            AREngineSession.SetDisplayGeometry(harmonyOrientation, width, height);
        }

        public ARAnchor CreateAnchor(Pose pose)
        {
            ARDebug.LogInfo("bridge acquire anchor begin with pose:{0}", pose.ToString());
            int anchorId = AREngineSession.CreateAnchor(ARBridge.FromUnityPose(pose));
            ARDebug.LogInfo("bridge acquire anchor end with id={0}", anchorId);
            if (anchorId < 0)
            {
                return null;
            }
            return m_ndkSession.AnchorManager.ARAnchorFactory(ARBridge.Handle(anchorId), true);
        }

        public void GetAllAnchors(List<ARAnchor> anchorList)
        {
            anchorList.Clear();
            int count = AREngineSession.GetAnchors(m_anchorBatch, MaxBatchRecords);
            for (int i = 0; i < count; i++)
            {
                IntPtr anchorHandle = ARBridge.Handle(m_anchorBatch[i].anchorId);
                anchorList.Add(m_ndkSession.AnchorManager.ARAnchorFactory(anchorHandle, false));
            }
        }

        //this function will add newly trackable into trackableManager
        public void GetAllTrackables(List<ARTrackable> trackableList)
        {
            trackableList.Clear();
            int count = AREngineSession.GetTrackables(AREngine_ARTrackableType.ARENGINE_TRACKABLE_BASE,
                m_trackableBatch, MaxBatchRecords);
            for (int i = 0; i < count; i++)
            {
                IntPtr trackableHandle = ARBridge.Handle(m_trackableBatch[i].trackableId);
                trackableList.Add(m_ndkSession.TrackableManager.ARTrackableFactory(trackableHandle, true));
            }
        }

        public long GetSaveLimit()
        {
            // World-map save and load exist only in AREngine's ArkTS layer
            // (AREngineRecorder / AREngineWorldMap), so there is nothing for the
            // bridge to call and no buffer size to report.
            ARBridge.NotSupported("ARSession world-map save and load");
            return 0;
        }

        public ARSharedData SaveSharedData()
        {
            long dataSize = GetSaveLimit();
            ARSharedData.ARRawData rawdata = new ARSharedData.ARRawData(dataSize);
            rawdata.DataSize = 0;
            return new ARSharedData(rawdata);
        }

        public void LoadSharedData(ARSharedData sharedData)
        {
            ARBridge.NotSupported("ARSession world-map save and load");
        }

        public long GetSerializeAnchorsLimit()
        {
            // Anchors are serialised by AREngine's cloud-anchor and ArkTS
            // ARAnchor.serialize APIs, neither of which is in the native C API.
            ARBridge.NotSupported("ARSession anchor serialisation");
            return 0;
        }

        public ARSharedData SerializeAnchors(List<ARAnchor> anchorList, bool isNeedAlign)
        {
            long dataSize = GetSerializeAnchorsLimit();
            ARSharedData.ARRawData rawData = new ARSharedData.ARRawData(dataSize);
            rawData.DataSize = 0;
            return new ARSharedData(rawData);
        }

        public void DeSerializeAnchors(ARSharedData sharedData, List<ARAnchor> anchors)
        {
            ARBridge.NotSupported("ARSession anchor serialisation");
            anchors.Clear();
        }

        public ARCameraConfig GetCameraConfig()
        {
            // The config adapter asks the bridge for the sizes and frees the native
            // object before returning, so the handle here is a token into a managed
            // table rather than a pointer ARCameraConfig has to release.
            IntPtr cameraConfigHandle = m_ndkSession.CameraConfigAdapter.Create();
            ARCameraConfig cameraConfig = new ARCameraConfig(cameraConfigHandle, m_ndkSession);
            return cameraConfig;
        }

        public void SetCloudServiceAuthInfo(string authInfo)
        {
            // Cloud anchors and the cloud image service are configured from ArkTS
            // (AREngineVirtualType / auth JSON), which the native C API cannot see.
            ARBridge.NotSupported("ARSession.SetCloudServiceAuthInfo");
        }
    }
}
