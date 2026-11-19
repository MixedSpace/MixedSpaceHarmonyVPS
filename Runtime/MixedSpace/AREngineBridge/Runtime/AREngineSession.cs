// Hand-written session driver for libAREngineBridge.so.
//
// This is the layer application code should use. It owns the scratch buffers so
// a frame costs no allocations, and it drives the render-thread handshake that
// AREngine needs because it renders the camera preview into an OES texture that
// only exists inside the engine's GL context.
//
// Anything not wrapped here is still reachable through AREngineExtended (the
// TJAREX_* conveniences) and AREngineNative (a 1:1 transcription of all 205
// HMS_AREngine_* C functions).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AREngineBridge
{
    /// <summary>
    ///     drives one AREngine session: lifecycle, camera texture, display
    ///     geometry and the per-frame update
    /// </summary>
    /// <remarks>
    ///     trackable ids stay valid for the whole session, so a caller may cache a
    ///     plane's id across frames and keep using it. Record indices are a
    ///     different thing: an index into a face, body, image or target batch
    ///     addresses the batch that getter last returned, so re-query the batch
    ///     after each <see cref="Update"/> instead of holding on to an index
    /// </remarks>
    public static partial class AREngineSession
    {
        /// <summary>records read per batched call unless the caller says otherwise</summary>
        public const int DefaultMaxRecords = 64;

        private const int IntrinsicsFloats = 11;
        private const int PoseRawFloats = 7;

        private static readonly float[] _frameData = new float[AREngineX.Frame.DataFloats];
        private static readonly float[] _poseRaw = new float[PoseRawFloats];
        private static readonly float[] _intrinsicsData = new float[IntrinsicsFloats];

        private static float[] _records = new float[DefaultMaxRecords * AREngineX.RecordFloats];
        private static float[] _floats = new float[DefaultMaxRecords * 4];
        private static int[] _ints = new int[DefaultMaxRecords * 3];
        private static byte[] _bytes = new byte[256];

        private static IntPtr _renderEventFunc = IntPtr.Zero;
        private static Texture2D _cameraTexture;
        private static uint _cameraTextureId;
        private static AREngineFrame _currentFrame;
        private static bool _isCreated;

        /// <summary>
        ///     how long to wait for the render thread, in milliseconds; one frame is
        ///     normally plenty and a timeout is recovered from rather than fatal
        /// </summary>
        public static int renderEventTimeoutMs = 100;

        /// <summary>
        ///     true once Create has succeeded and Destroy has not been called
        /// </summary>
        public static bool IsCreated => _isCreated;

        /// <summary>
        ///     the OES texture id AREngine renders the camera preview into, or 0
        ///     before InitialiseCameraTexture
        /// </summary>
        public static uint CameraTextureId => _cameraTextureId;

        /// <summary>
        ///     the frame read by the most recent Update
        /// </summary>
        public static AREngineFrame CurrentFrame => _currentFrame;

        #region Capability and diagnostics

        /// <summary>
        ///     checks that the loaded libAREngineBridge.so speaks the ABI these
        ///     bindings were generated for
        /// </summary>
        /// <returns>true when the native library matches</returns>
        public static bool CheckNativeVersion()
        {
            var native = AREngineExtended.GetBridgeVersion();
            if (native != AREngineX.BridgeVersion)
            {
                Debug.LogError($"arengine: error: bridge version mismatch: native {native}, expected {AREngineX.BridgeVersion}");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     asks whether this device can run a feature
        /// </summary>
        /// <param name="featureType">the feature to probe</param>
        /// <returns>true when supported on this device</returns>
        public static bool IsSupported(AREngine_FeatureType featureType)
        {
            return AREngineExtended.IsSupported(featureType) == AREngine_ARStatus.ARENGINE_SUCCESS;
        }

        /// <summary>
        ///     turns a status code into the text the bridge reports for it
        /// </summary>
        /// <param name="status">the code to describe</param>
        /// <returns>a short human-readable description</returns>
        public static string GetStatusText(AREngine_ARStatus status)
        {
            var pointer = AREngineExtended.GetStatusString((int)status);
            return pointer == IntPtr.Zero ? status.ToString() : Marshal.PtrToStringAnsi(pointer);
        }

        #endregion

        #region Lifecycle

        /// <summary>
        ///     creates the session and applies the given configuration
        /// </summary>
        /// <param name="config">the settings to apply, or null for the defaults</param>
        /// <returns>ARENGINE_SUCCESS when the session is ready to resume</returns>
        /// <remarks>
        ///     creating a second time without destroying first is refused rather
        ///     than leaked; Huawei's own Unity SDK had to add the same guard after
        ///     a double-destroy in a finaliser crashed the process
        /// </remarks>
        public static AREngine_ARStatus Create(AREngineSessionConfig config = null)
        {
            if (_isCreated)
            {
                Debug.LogWarning("arengine: session already created, ignoring Create");
                return AREngine_ARStatus.ARENGINE_SUCCESS;
            }

            if (!CheckNativeVersion())
            {
                return AREngine_ARStatus.ARENGINE_ERROR_FATAL;
            }

            var status = AREngineExtended.CreateSession(IntPtr.Zero, IntPtr.Zero);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogError($"arengine: error: failed to create session: {GetStatusText(status)}");
                return status;
            }

            _isCreated = true;
            status = ApplyConfig(config ?? new AREngineSessionConfig());
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogError($"arengine: error: failed to apply configuration: {GetStatusText(status)}");
                Destroy();
                return status;
            }

            return AREngine_ARStatus.ARENGINE_SUCCESS;
        }

        /// <summary>
        ///     applies a configuration to an existing session
        /// </summary>
        /// <param name="config">the settings to apply</param>
        /// <returns>ARENGINE_SUCCESS when every setting was accepted</returns>
        /// <remarks>
        ///     the whole batch is rolled back if any single setter fails, so the
        ///     session is never left holding a half-applied configuration
        /// </remarks>
        public static AREngine_ARStatus ApplyConfig(AREngineSessionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var status = AREngineExtended.ConfigBegin();
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = ApplyConfigFields(config);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                AREngineExtended.ConfigCancel();
                return status;
            }

            return AREngineExtended.ConfigCommit();
        }

        /// <summary>
        ///     resumes tracking; the camera permission must already be granted
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when the camera is running</returns>
        public static AREngine_ARStatus Resume()
        {
            return ReportStatus("resume", AREngineExtended.Resume());
        }

        /// <summary>
        ///     pauses tracking without losing the map
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when paused</returns>
        public static AREngine_ARStatus Pause()
        {
            return ReportStatus("pause", AREngineExtended.Pause());
        }

        /// <summary>
        ///     stops tracking and discards the map
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when stopped</returns>
        public static AREngine_ARStatus Stop()
        {
            return ReportStatus("stop", AREngineExtended.Stop());
        }

        /// <summary>
        ///     releases the session, every anchor and the camera texture
        /// </summary>
        /// <remarks>
        ///     call this before the engine shuts down. Destroying twice is safe,
        ///     and the bridge clears its trackable table so no stale id survives
        /// </remarks>
        public static void Destroy()
        {
            if (_cameraTextureId != 0)
            {
                IssueRenderEvent(AREngineX.Event.DeleteCameraTexture);
                _cameraTextureId = 0;
            }

            if (_cameraTexture != null)
            {
                UnityEngine.Object.Destroy(_cameraTexture);
                _cameraTexture = null;
            }

            AREngineExtended.DestroySession();
            _isCreated = false;
            _currentFrame = default;
            _renderEventFunc = IntPtr.Zero;
        }

        #endregion

        #region Camera texture and display geometry

        /// <summary>
        ///     creates the OES texture AREngine renders into and binds it to the
        ///     session
        /// </summary>
        /// <returns>the texture id, or 0 on failure</returns>
        /// <remarks>
        ///     both steps run on the render thread because glGenTextures and
        ///     ARSession_SetCameraTextureName need the engine's GL context current
        /// </remarks>
        public static uint InitialiseCameraTexture()
        {
            if (!IssueRenderEvent(AREngineX.Event.CreateCameraTexture))
            {
                return 0;
            }

            _cameraTextureId = AREngineExtended.GetCameraTextureId();
            if (_cameraTextureId == 0)
            {
                Debug.LogError("arengine: error: failed to create camera texture");
                return 0;
            }

            if (!IssueRenderEvent(AREngineX.Event.BindCameraTexture))
            {
                return 0;
            }

            return _cameraTextureId;
        }

        /// <summary>
        ///     wraps the camera texture so a material can sample it
        /// </summary>
        /// <returns>the external texture, recreated if the id has changed</returns>
        /// <remarks>
        ///     the returned object must be sampled as samplerExternalOES; a plain
        ///     sampler2D will read nothing. It is also not readable from the CPU, so
        ///     pixel access has to go through CopyCameraImagePlane instead
        /// </remarks>
        public static Texture2D GetCameraTexture()
        {
            if (_cameraTextureId == 0)
            {
                Debug.LogWarning("arengine: camera texture requested before InitialiseCameraTexture");
                return null;
            }

            var native = new IntPtr((long)_cameraTextureId);
            if (_cameraTexture == null)
            {
                // Width and height are unknown for an external OES texture; the
                // driver reports them, which is what Huawei's Unity SDK relies on
                // too when it passes zeros here.
                _cameraTexture = Texture2D.CreateExternalTexture(0, 0, TextureFormat.ARGB32, false, false, native);
                return _cameraTexture;
            }

            _cameraTexture.UpdateExternalTexture(native);
            return _cameraTexture;
        }

        /// <summary>
        ///     tells AREngine how the preview is oriented on screen
        /// </summary>
        /// <param name="rotation">the display rotation</param>
        /// <param name="width">view width in pixels</param>
        /// <param name="height">view height in pixels</param>
        /// <returns>ARENGINE_SUCCESS when accepted</returns>
        /// <remarks>
        ///     the sizes must match the real view or the preview and the UVs will
        ///     be misaligned
        /// </remarks>
        public static AREngine_ARStatus SetDisplayGeometry(AREngine_ARPoseType rotation, int width, int height)
        {
            return ReportStatus("set display geometry",
                AREngineExtended.SetDisplayGeometry((int)rotation, width, height));
        }

        /// <summary>
        ///     sets the display geometry from the current screen orientation and size
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when accepted</returns>
        public static AREngine_ARStatus SetDisplayGeometryFromScreen()
        {
            return SetDisplayGeometry(RotationForOrientation(Screen.orientation), Screen.width, Screen.height);
        }

        /// <summary>
        ///     maps a Unity screen orientation onto the rotation AREngine expects
        /// </summary>
        /// <param name="orientation">the current screen orientation</param>
        /// <returns>the matching display rotation</returns>
        public static AREngine_ARPoseType RotationForOrientation(ScreenOrientation orientation)
        {
            switch (orientation)
            {
                case ScreenOrientation.LandscapeLeft:
                    return AREngine_ARPoseType.ARENGINE_POSE_TYPE_ROTATE_90;
                case ScreenOrientation.LandscapeRight:
                    return AREngine_ARPoseType.ARENGINE_POSE_TYPE_ROTATE_270;
                case ScreenOrientation.PortraitUpsideDown:
                    return AREngine_ARPoseType.ARENGINE_POSE_TYPE_ROTATE_180;
                default:
                    return AREngine_ARPoseType.ARENGINE_POSE_TYPE_IDENTITY;
            }
        }

        /// <summary>
        ///     rewrites quad UVs so the preview fills the screen at the current
        ///     orientation
        /// </summary>
        /// <param name="uvsIn">source UV pairs, interleaved [x0,y0,x1,y1,...]</param>
        /// <param name="uvsOut">destination, at least as long as uvsIn</param>
        /// <returns>ARENGINE_SUCCESS when transformed</returns>
        /// <remarks>
        ///     AREngine's <c>elementSize</c> is the number of FLOATS in the arrays
        ///     (the array length), not the number of UV pairs: the header states
        ///     "number of texture coordinates to be converted ... a multiple of 2"
        ///     and "uvsOut ... the array size is elementSize". Passing uvsIn.Length/2
        ///     makes AREngine transform only the first half of the quad, leaving the
        ///     right edge at (0,0) - a degenerate UV that stretches one texel column
        ///     across half the screen along a diagonal seam.
        /// </remarks>
        public static AREngine_ARStatus TransformDisplayUvCoords(float[] uvsIn, float[] uvsOut)
        {
            if (uvsIn == null || uvsOut == null || uvsOut.Length < uvsIn.Length)
            {
                throw new ArgumentException("uvsOut must be at least as long as uvsIn");
            }

            return AREngineExtended.TransformDisplayUvCoords(uvsIn, uvsOut, uvsIn.Length);
        }

        /// <summary>
        ///     sets the near and far planes used to build the projection matrix
        /// </summary>
        /// <param name="nearPlane">near plane in metres</param>
        /// <param name="farPlane">far plane in metres</param>
        public static void SetClipPlanes(float nearPlane, float farPlane)
        {
            AREngineExtended.SetClipPlanes(nearPlane, farPlane);
        }

        #endregion

        #region Per frame

        /// <summary>
        ///     advances AREngine on the render thread and reads the new frame
        /// </summary>
        /// <returns>true when a usable frame was read</returns>
        /// <remarks>
        ///     call this once per rendered frame, before anything reads
        ///     CurrentFrame or the trackable getters. It blocks until the render
        ///     thread has finished, so the pose and the camera texture always
        ///     describe the same instant
        /// </remarks>
        public static bool Update()
        {
            if (!_isCreated)
            {
                return false;
            }

            if (!IssueRenderEvent(AREngineX.Event.Update))
            {
                return false;
            }

            var status = AREngineExtended.GetFrameData(_frameData, _frameData.Length);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                _currentFrame.isValid = false;
                return false;
            }

            _currentFrame.isValid = true;
            _currentFrame.rawCameraPose = AREngineRecord.ReadPose(_frameData, AREngineX.Frame.PoseQuat);
            _currentFrame.viewMatrix = AREngineRecord.ReadMatrix(_frameData, AREngineX.Frame.ViewMatrix);
            _currentFrame.projectionMatrix = AREngineRecord.ReadMatrix(_frameData, AREngineX.Frame.ProjMatrix);
            _currentFrame.trackingState =
                (AREngine_ARTrackingState)(int)_frameData[AREngineX.Frame.TrackingState];
            _currentFrame.trackingStateReason =
                (AREngine_ARTrackingStateReason)(int)_frameData[AREngineX.Frame.StateReason];
            _currentFrame.timestampNs = AREngineExtended.GetFrameTimestampNs();

            // The display-oriented pose is what a Unity camera wants; the raw pose
            // above ignores screen rotation.
            if (AREngineExtended.GetDisplayOrientedPose(_poseRaw) == AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                _currentFrame.cameraPose = AREngineRecord.ReadPose(_poseRaw, 0);
            }
            else
            {
                _currentFrame.cameraPose = _currentFrame.rawCameraPose;
            }

            return true;
        }

        /// <summary>
        ///     true when the display geometry changed this frame and the projection
        ///     matrix needs rebuilding
        /// </summary>
        public static bool DisplayGeometryChanged => AREngineExtended.GetDisplayGeometryChanged() != 0;

        /// <summary>
        ///     reads the camera's intrinsic parameters
        /// </summary>
        /// <param name="intrinsics">the intrinsics, when the call succeeds</param>
        /// <returns>true when at least the focal length and principal point were read</returns>
        public static bool TryGetCameraIntrinsics(out AREngineCameraIntrinsics intrinsics)
        {
            intrinsics = default;
            var written = AREngineExtended.GetCameraIntrinsics(_intrinsicsData, _intrinsicsData.Length);
            if (written < 4)
            {
                return false;
            }

            intrinsics.focalLengthX = _intrinsicsData[0];
            intrinsics.focalLengthY = _intrinsicsData[1];
            intrinsics.principalPointX = _intrinsicsData[2];
            intrinsics.principalPointY = _intrinsicsData[3];
            if (written >= 6)
            {
                intrinsics.imageWidth = (int)_intrinsicsData[4];
                intrinsics.imageHeight = (int)_intrinsicsData[5];
            }

            if (written >= IntrinsicsFloats)
            {
                intrinsics.distortion = new float[IntrinsicsFloats - 6];
                Array.Copy(_intrinsicsData, 6, intrinsics.distortion, 0, intrinsics.distortion.Length);
            }

            return true;
        }

        /// <summary>
        ///     reads the camera's projection matrix for one near/far pair
        /// </summary>
        /// <param name="nearPlane">near clip distance in metres</param>
        /// <param name="farPlane">far clip distance in metres</param>
        /// <param name="matrix">the column-major matrix, when the call succeeds</param>
        /// <returns>
        ///     true when the matrix was read. False means there is no camera yet,
        ///     so the caller has not had a successful <see cref="Update"/>
        /// </returns>
        /// <remarks>
        ///     this does not move the clip planes <see cref="SetClipPlanes"/>
        ///     controls, so it is safe to call while building a camera
        /// </remarks>
        public static bool TryGetCameraProjectionMatrix(float nearPlane, float farPlane, out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            if (AREngineExtended.GetCameraProjectionMatrix(FloatBuffer(16), nearPlane, farPlane) !=
                AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            matrix = AREngineRecord.ReadMatrix(_floats, 0);
            return true;
        }

        #endregion

        #region Internals

        private static AREngine_ARStatus ApplyConfigFields(AREngineSessionConfig config)
        {
            var status = AREngineExtended.ConfigSetARType((int)config.arType);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("plane finding mode", () => AREngineExtended.ConfigSetPlaneFindingMode(config.planeFindingMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("focus mode", () => AREngineExtended.ConfigSetFocusMode(config.focusMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("update mode", () => AREngineExtended.ConfigSetUpdateMode(config.updateMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("power mode", () => AREngineExtended.ConfigSetPowerMode(config.powerMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("depth mode", () => AREngineExtended.ConfigSetDepthMode(config.depthMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("mesh mode", () => AREngineExtended.ConfigSetMeshMode(config.meshMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("semantic mode", () => AREngineExtended.ConfigSetSemanticMode(config.semanticMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("semantic dense mode",
                () => AREngineExtended.ConfigSetSemanticDenseMode(config.semanticDenseMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("image stream mode",
                () => AREngineExtended.ConfigSetImageStreamMode(config.imageStreamMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("pose mode", () => AREngineExtended.ConfigSetPoseMode(config.poseMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("multi face mode", () => AREngineExtended.ConfigSetMultiFaceMode(config.multiFaceMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("camera lens facing",
                () => AREngineExtended.ConfigSetCameraLensFacing(config.cameraLensFacing));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            status = Set("camera preview mode",
                () => AREngineExtended.ConfigSetCameraPreviewMode(config.previewMode));
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return status;
            }

            if (config.previewWidth > 0 && config.previewHeight > 0)
            {
                status = Set("preview size",
                    () => AREngineExtended.ConfigSetPreviewSize(config.previewWidth, config.previewHeight));
                if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
                {
                    return status;
                }
            }

            if (config.maxMapSize > 0)
            {
                status = Set("max map size",
                    () => AREngineExtended.ConfigSetMaxMapSize((ulong)config.maxMapSize));
                if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
                {
                    return status;
                }
            }

            if (config.bodyDetectedNum > 0)
            {
                status = Set("body detected num",
                    () => AREngineExtended.ConfigSetBodyDetectedNum(config.bodyDetectedNum));
                if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
                {
                    return status;
                }
            }

            return AREngine_ARStatus.ARENGINE_SUCCESS;
        }

        private static AREngine_ARStatus Set(string what, Func<AREngine_ARStatus> setter)
        {
            var status = setter();
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogError($"arengine: error: failed to set {what}: {GetStatusText(status)}");
            }

            return status;
        }

        private static AREngine_ARStatus ReportStatus(string what, AREngine_ARStatus status)
        {
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogError($"arengine: error: failed to {what}: {GetStatusText(status)}");
            }

            return status;
        }

        private static bool IssueRenderEvent(int eventId)
        {
            if (_renderEventFunc == IntPtr.Zero)
            {
                _renderEventFunc = AREngineExtended.GetRenderEventFunc();
            }

            AREngineExtended.BeginRenderEvent();
            GL.IssuePluginEvent(_renderEventFunc, eventId);
            if (AREngineExtended.WaitForRenderEvent(renderEventTimeoutMs) != 0)
            {
                return true;
            }

            Debug.LogWarning($"arengine: render event {eventId} did not finish within {renderEventTimeoutMs} ms");
            return false;
        }

        /// <summary>
        ///     returns a record buffer holding at least maxRecords records
        /// </summary>
        /// <param name="maxRecords">how many 16-float records the caller will read</param>
        /// <returns>the shared scratch buffer, grown if necessary</returns>
        private static float[] RecordBuffer(int maxRecords)
        {
            var needed = maxRecords * AREngineX.RecordFloats;
            if (_records.Length < needed)
            {
                _records = new float[needed];
            }

            return _records;
        }

        /// <summary>
        ///     returns a float scratch buffer of at least the given length
        /// </summary>
        /// <param name="floatCount">how many floats the caller will read</param>
        /// <returns>the shared scratch buffer, grown if necessary</returns>
        private static float[] FloatBuffer(int floatCount)
        {
            if (_floats.Length < floatCount)
            {
                _floats = new float[floatCount];
            }

            return _floats;
        }

        /// <summary>
        ///     returns an int scratch buffer of at least the given length
        /// </summary>
        /// <param name="intCount">how many ints the caller will read</param>
        /// <returns>the shared scratch buffer, grown if necessary</returns>
        private static int[] IntBuffer(int intCount)
        {
            if (_ints.Length < intCount)
            {
                _ints = new int[intCount];
            }

            return _ints;
        }

        /// <summary>
        ///     returns a byte scratch buffer of at least the given length
        /// </summary>
        /// <param name="byteCount">how many bytes the caller will read</param>
        /// <returns>the shared scratch buffer, grown if necessary</returns>
        private static byte[] ByteBuffer(int byteCount)
        {
            if (_bytes.Length < byteCount)
            {
                _bytes = new byte[byteCount];
            }

            return _bytes;
        }

        /// <summary>
        ///     reports a negative batched-getter result, which is a negated status
        /// </summary>
        /// <param name="what">the call that failed, for the log message</param>
        /// <param name="result">the negative return value</param>
        /// <returns>always 0, so the caller yields an empty result set</returns>
        private static int Failed(string what, int result)
        {
            Debug.LogError($"arengine: error: failed to read {what}: {GetStatusText((AREngine_ARStatus)(-result))}");
            return 0;
        }

        #endregion
    }
}
