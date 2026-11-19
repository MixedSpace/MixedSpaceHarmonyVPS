namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using AREngineBridge;

    // The camera configuration, on top of the AREngineBridge.
    //
    // This one goes through the raw TJARE_* layer rather than AREngineSession,
    // because the bridge's convenience layer stops at session configuration and
    // never wrapped ARCameraConfig. The two calls are the ones the old adapter made,
    // in the same order: create an empty config object, then let the session fill it.
    //
    // What comes back from Create is not that native pointer, though. ARCameraConfig
    // keeps the handle in a field and frees it from a finalizer, and a finalizer runs
    // on whichever thread the GC picks - after ARSession.Stop and
    // AREngineSession.Destroy have been reached, that is a native object the session
    // may already have taken back. The only two facts a config is ever asked for are
    // its image and texture sizes, so both are read here and then, the native object
    // is destroyed on the calling thread before Create returns, and the handle is a
    // slot in a managed table.
    internal class ARCameraConfigAdapter
    {
        /// <summary>the two size pairs one camera config can answer with</summary>
        private struct CameraConfigSnapshot
        {
            public Vector2Int ImageDimensions;
            public Vector2Int TextureDimensions;
        }

        private readonly BridgeTokenTable<CameraConfigSnapshot> m_Configs =
            new BridgeTokenTable<CameraConfigSnapshot>();

        private NDKSession m_ndkSession;

        public ARCameraConfigAdapter(NDKSession session)
        {
            m_ndkSession = session;
        }

        public IntPtr Create()
        {
            IntPtr sessionHandle = m_ndkSession.SessionHandle;
            if (sessionHandle == IntPtr.Zero)
            {
                ARDebug.LogError("ARCameraConfig.Create skipped: the bridge has no session yet");
                return IntPtr.Zero;
            }

            IntPtr configPtr = IntPtr.Zero;
            if (!ARBridge.Ok("ARCameraConfig.Create", AREngineNative.ARCameraConfig_Create(sessionHandle, out configPtr))
                || configPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            AREngine_ARStatus status = AREngineNative.ARSession_GetCameraConfig(sessionHandle, configPtr);

            CameraConfigSnapshot snapshot = new CameraConfigSnapshot();
            snapshot.ImageDimensions = ReadDimensions(configPtr, true);
            snapshot.TextureDimensions = ReadDimensions(configPtr, false);

            AREngineNative.ARCameraConfig_Destroy(configPtr);

            // A refused getCameraConfig leaves both size pairs at zero rather than
            // failing the call outright, which is what the old layer handed back too;
            // Ok() is what keeps it from being silent.
            ARBridge.Ok("ARSession.GetCameraConfig", status);
            return m_Configs.Store(snapshot);
        }

        public void Destory(IntPtr cameraConfigHandle)
        {
            m_Configs.Release(cameraConfigHandle);
        }

        public Vector2Int GetImageDimensions(IntPtr cameraConfigHandle)
        {
            CameraConfigSnapshot snapshot;
            return m_Configs.TryGet(cameraConfigHandle, out snapshot) ? snapshot.ImageDimensions : Vector2Int.zero;
        }

        public Vector2Int GetTextureDimensions(IntPtr cameraConfigHandle)
        {
            CameraConfigSnapshot snapshot;
            return m_Configs.TryGet(cameraConfigHandle, out snapshot) ? snapshot.TextureDimensions : Vector2Int.zero;
        }

        private Vector2Int ReadDimensions(IntPtr configPtr, bool image)
        {
            int width = 0;
            int height = 0;
            AREngine_ARStatus status = image
                ? AREngineNative.ARCameraConfig_GetImageDimensions(m_ndkSession.SessionHandle, configPtr, out width,
                    out height)
                : AREngineNative.ARCameraConfig_GetTextureDimensions(m_ndkSession.SessionHandle, configPtr, out width,
                    out height);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return Vector2Int.zero;
            }
            return new Vector2Int(width, height);
        }
    }
}
