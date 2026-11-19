// MixedSpace platform support backed by libAREngineBridge.so.
//
// VPS input follows the AREnginePlatformSupport path used by MixedSpaceStudio2:
// copy the CPU luma plane in its native row order and use the pose from the
// same AREngine frame. Keep the existing image-intrinsics alignment.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AREngineBridge;
using MixedSpace;
using MixedSpace.XR;
using UnityEngine;

namespace AREngineBridge.Glue
{
    /// <summary>
    ///     feeds MixedSpace's server localisation with camera frames and poses read
    ///     through the AREngine bridge
    /// </summary>
    /// <remarks>
    ///     something else must drive <see cref="AREngineSession"/>; this component only
    ///     reads the frame that is already current. Attach it to the same GameObject
    ///     the session driver lives on and point MixedSpaceSDK's platform field here
    /// </remarks>
    public sealed class AREnginePlatformSupport : MonoBehaviour, IMixedSpacePlatformAdapter, IMixedSpaceTrackingLossSource, IMixedSpaceCaptureDiagnostics
    {
        // YUV_420_888 plane 0 is luma, which is the single channel the localiser wants.
        private const int LumaPlane = 0;

        // How often to repeat a frame-skip warning, so a starved camera does not
        // flood hilog at frame rate.
        private const float DiagnoseIntervalSeconds = 2f;
        private const float CameraStartupTimeoutSeconds = 10f;
        private const int CameraProbeDelayMilliseconds = 100;

        private Task<IMixedSpacePlatformFrame> _currentCameraDataTask;
        private bool _isConfigured;
        private bool _writePoseLogs;
        private float _nextTrackingDiagnosticTime;

        public void SetWritePoseLogs(bool enabled) => _writePoseLogs = enabled;

        private void LateUpdate()
        {
            if (!_isConfigured) return;
            MixedSpacePlatformPoseSample sample = SamplePose();
            if (!_writePoseLogs || Time.realtimeSinceStartup < _nextTrackingDiagnosticTime) return;
            _nextTrackingDiagnosticTime = Time.realtimeSinceStartup + 0.2f;
            MixedSpacePoseDiagnostics.AppendTrackingSample(sample, "harmony_scene_camera");
        }
        private int _startupAttempt;
        private bool _wasTracking;
        private long _lastTrackingTimestampNanoseconds;

        public long LastTrackingLossTimestampNanoseconds { get; private set; }
        private Camera _camera;
        private float _lastDiagnoseTime = -1000f;

        [SerializeField] private MixedSpacePixelFormat _cameraDataFormat = MixedSpacePixelFormat.Luminance8;

        /// <summary>
        ///     the camera format callers asked for; the CPU path can only ever serve
        ///     a single channel, so this is reported but not acted on
        /// </summary>
        public MixedSpacePixelFormat CameraDataFormat => _cameraDataFormat;

        /// <summary>
        ///     whether the AREngine session is up and producing frames
        /// </summary>
        /// <returns>true when the session exists</returns>
        public bool IsSessionReady()
        {
            return AREngineSession.IsCreated;
        }

        public async Task<bool> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int attempt = ++_startupAttempt;
            _isConfigured = false;
            float startedAt = Time.realtimeSinceStartup;
            Debug.Log("arengine: waiting for camera readiness");
            while (this != null && isActiveAndEnabled && attempt == _startupAttempt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryPrepareCamera(out int imgWidth, out int imgHeight, out string reason))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _isConfigured = true;
                    Debug.Log($"arengine: camera ready after {Time.realtimeSinceStartup - startedAt:F2}s: " +
                              $"image={imgWidth}x{imgHeight}, frame={AREngineSession.CurrentFrame.timestampNs}");
                    return true;
                }

                Diagnose(reason);
                if (Time.realtimeSinceStartup - startedAt >= CameraStartupTimeoutSeconds)
                {
                    Debug.LogError($"arengine: camera startup timed out after {CameraStartupTimeoutSeconds:F0}s: {reason}");
                    return false;
                }

                // Resume on Unity's context; no borrowed image survives this wait.
                await Task.Delay(CameraProbeDelayMilliseconds, cancellationToken);
            }
            return false;
        }

        public Task<IMixedSpacePlatformFrame> CaptureFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isConfigured || !isActiveAndEnabled)
                return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                    new MixedSpaceTrackingSnapshot(0), null));
            _currentCameraDataTask = GetCameraData(cancellationToken);
            return _currentCameraDataTask;
        }

        public MixedSpacePlatformPoseSample SamplePose()
        {
            Camera cam = GetCamera();
            AREngineFrame frame = IsSessionReady() ? AREngineSession.CurrentFrame : default;
            long timestampNanoseconds = frame.isValid ? frame.timestampNs : 0;
            bool tracking = _isConfigured && isActiveAndEnabled && cam != null && frame.isValid &&
                frame.trackingState == AREngine_ARTrackingState.ARENGINE_TRACKING_STATE_TRACKING && timestampNanoseconds > 0;
            ObserveTrackingState(tracking, timestampNanoseconds);
            return new MixedSpacePlatformPoseSample
            {
                Position = cam != null ? cam.transform.position : Vector3.zero,
                Rotation = cam != null ? cam.transform.rotation : Quaternion.identity,
                TimestampNanoseconds = timestampNanoseconds,
                TimestampSource = MixedSpaceTrackingTimestampSource.Device,
                IsTracking = tracking
            };
        }

        private void ObserveTrackingState(bool tracking, long timestampNanoseconds)
        {
            if (_wasTracking && !tracking)
                LastTrackingLossTimestampNanoseconds = Math.Max(
                    LastTrackingLossTimestampNanoseconds, _lastTrackingTimestampNanoseconds);
            if (tracking && timestampNanoseconds > 0)
                _lastTrackingTimestampNanoseconds = timestampNanoseconds;
            _wasTracking = tracking;
        }

        public async Task ShutdownAsync()
        {
            ObserveTrackingState(false, 0);
            ++_startupAttempt;
            _isConfigured = false;
            if (_currentCameraDataTask != null)
                await _currentCameraDataTask;
        }

        private void OnDisable()
        {
            ObserveTrackingState(false, 0);
            ++_startupAttempt;
            _isConfigured = false;
        }

        private bool TryPrepareCamera(out int imgWidth, out int imgHeight, out string reason)
        {
            imgWidth = 0;
            imgHeight = 0;
            reason = "session not created";
            if (!IsSessionReady())
                return false;

            AREngineFrame frame = AREngineSession.CurrentFrame;
            reason = "current frame not available";
            if (!frame.isValid || frame.timestampNs <= 0)
                return false;
            // Session startup needs a readable camera, not an already initialized SLAM pose.
            // CaptureFrameAsync/GetCameraData and SamplePose still reject untracked frames.
            reason = "scene camera missing";
            if (GetCamera() == null)
                return false;

            reason = "CPU camera image not available";
            if (!TryAcquireCameraImageY(out _, out imgWidth, out imgHeight, copyPixels: false))
                return false;
            reason = "camera intrinsics not valid";
            if (!GetIntrinsicsForImage(imgWidth, imgHeight, out Vector4 intrinsics) ||
                !IsFinite(intrinsics.x) || !IsFinite(intrinsics.y) ||
                !IsFinite(intrinsics.z) || !IsFinite(intrinsics.w) ||
                intrinsics.x <= 0f || intrinsics.y <= 0f)
                return false;

            reason = null;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private Task<IMixedSpacePlatformFrame> GetCameraData(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AREngineFrame frame = IsSessionReady() ? AREngineSession.CurrentFrame : default;
            long timestampNanoseconds = frame.isValid ? frame.timestampNs : 0;
            if (!frame.isValid || timestampNanoseconds <= 0 ||
                frame.trackingState != AREngine_ARTrackingState.ARENGINE_TRACKING_STATE_TRACKING)
                return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                    new MixedSpaceTrackingSnapshot(0), null));
            Camera cam = GetCamera();
            if (!IsSessionReady() || cam == null ||
                !TryAcquireCameraImageY(out byte[] imageBytes, out int imgWidth, out int imgHeight) ||
                !GetIntrinsicsForImage(imgWidth, imgHeight, out Vector4 intrinsics))
                return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                    new MixedSpaceTrackingSnapshot(0), null));

            cancellationToken.ThrowIfCancellationRequested();
            Pose framePose = frame.cameraPose;
            var poseData = new HuaweiARInternal.NDKPoseData
            {
                Tx = framePose.position.x,
                Ty = framePose.position.y,
                Tz = framePose.position.z,
                Qx = framePose.rotation.x,
                Qy = framePose.rotation.y,
                Qz = framePose.rotation.z,
                Qw = framePose.rotation.w,
            };
            Pose capturePose = poseData.ToUnityPose();
            // The luma plane and intrinsics use the sensor's orientation.
            // Display rotation still applies to pose conversion.
            var profile = new MixedSpaceCaptureProfile(
                imgWidth, imgHeight, 1, MixedSpacePixelFormat.Luminance8,
                intrinsics, capturePose.position, capturePose.rotation,
                timestampNanoseconds, GetOrientation(), nativeImageRotationAngle: 0);
            // The borrowed AREngine image has already been released by the CPU copy.
            // The new capture owns the pinned buffer until its final pixel lease ends.
            var capture = new MixedSpaceCameraCapture(profile, new MixedSpaceManagedPixelSource(imageBytes));
            return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                new MixedSpaceTrackingSnapshot(1), capture));
        }

        // Copies the luma plane in native row order, matching the working
        // MixedSpaceStudio2 upload path, and skips row-stride padding.
        private bool TryAcquireCameraImageY(out byte[] yBytes, out int imgWidth, out int imgHeight,
            bool copyPixels = true)
        {
            yBytes = null;
            imgWidth = 0;
            imgHeight = 0;

            if (!IsSessionReady())
            {
                Diagnose("no frame: session not created");
                return false;
            }

            if (!AREngineSession.AcquireCameraImage())
            {
                Diagnose("no frame: AcquireCameraImage failed, AREngine has no camera buffer");
                return false;
            }

            try
            {
                if (!AREngineSession.TryGetCameraImageInfo(out imgWidth, out imgHeight, out _, out _))
                {
                    imgWidth = 0;
                    imgHeight = 0;
                    return false;
                }

                if (imgWidth <= 0 || imgHeight <= 0)
                {
                    return false;
                }

                IntPtr plane = AREngineSession.GetCameraImagePlaneData(LumaPlane, out int rowStride, out _,
                    out int dataLength);
                if (plane == IntPtr.Zero)
                {
                    Diagnose("no frame: luma plane unavailable");
                    return false;
                }

                if (rowStride <= 0)
                {
                    rowStride = imgWidth;
                }

                if (dataLength < rowStride * (imgHeight - 1) + imgWidth)
                {
                    Diagnose($"no frame: luma plane too small, {dataLength} bytes for " +
                             $"{imgWidth}x{imgHeight} at stride {rowStride}");
                    return false;
                }

                // Startup only checks availability; it must not allocate or keep a stale image.
                if (!copyPixels)
                    return true;

                yBytes = new byte[imgWidth * imgHeight];
                for (int row = 0; row < imgHeight; row++)
                {
                    // Keep the sensor's first row first, skipping row padding.
                    Marshal.Copy(IntPtr.Add(plane, row * rowStride), yBytes, row * imgWidth,
                        imgWidth);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"arengine: error: failed to read camera image: {e.Message}");
                return false;
            }
            finally
            {
                AREngineSession.ReleaseCameraImage();
            }
        }

        /// <summary>
        ///     reads the camera intrinsics as (fx, fy, cx, cy)
        /// </summary>
        /// <param name="intrinsics">focal lengths and principal point in pixels</param>
        /// <returns>true when the device reported intrinsics</returns>
        public bool GetIntrinsics(out Vector4 intrinsics)
        {
            intrinsics = Vector4.zero;

            if (!AREngineSession.TryGetCameraIntrinsics(out AREngineCameraIntrinsics hw))
            {
                return false;
            }

            intrinsics = new Vector4(hw.focalLengthX, hw.focalLengthY, hw.principalPointX, hw.principalPointY);
            return true;
        }

        // Rotates the reported intrinsics into the orientation the image actually
        // arrived in. Some devices return display-oriented values for a sensor-oriented
        // CPU image, and feeding the server mismatched geometry makes the pose solve
        // wrong every time. Two independent signals are used to spot the transposition:
        // the reported size being the image's, swapped, and the principal point sitting
        // closer to the height centre than the width centre.
        //
        // The result is ordered (fx, fy, cx, cy), matching what RESTJobsAsync maps onto
        // fx/fy/ox/oy. The comment on CameraData.Intrinsics claims the opposite order;
        // that comment is stale and the consumers here are the authority.
        private bool GetIntrinsicsForImage(int imgWidth, int imgHeight, out Vector4 intrinsics)
        {
            intrinsics = Vector4.zero;

            if (!AREngineSession.TryGetCameraIntrinsics(out AREngineCameraIntrinsics hw))
            {
                return false;
            }

            bool transposed = (hw.imageWidth == imgHeight && hw.imageHeight == imgWidth) ||
                              Mathf.Abs(hw.principalPointX - imgWidth * 0.5f) >
                              Mathf.Abs(hw.principalPointX - imgHeight * 0.5f);
            if (transposed)
            {
                intrinsics = new Vector4(hw.focalLengthY, hw.focalLengthX, hw.principalPointY, hw.principalPointX);
                Debug.Log($"arengine: intrinsics were in display orientation, swapped: " +
                          $"fx={hw.focalLengthX} fy={hw.focalLengthY} cx={hw.principalPointX} cy={hw.principalPointY} " +
                          $"size={hw.imageWidth}x{hw.imageHeight} -> image={imgWidth}x{imgHeight}");
            }
            else
            {
                intrinsics = new Vector4(hw.focalLengthX, hw.focalLengthY, hw.principalPointX, hw.principalPointY);
                if (hw.imageWidth != imgWidth || hw.imageHeight != imgHeight)
                {
                    Debug.LogWarning($"arengine: intrinsics size {hw.imageWidth}x{hw.imageHeight} does not match " +
                                     $"image {imgWidth}x{imgHeight}");
                }
            }

            return true;
        }

        /// <summary>
        ///     the in-plane rotation the sensor image needs to line up with the display
        /// </summary>
        /// <returns>a rotation about Z, in degrees</returns>
        public Quaternion GetOrientation()
        {
            ScreenOrientation orientation = Screen.orientation;
            float angle = orientation switch
            {
                ScreenOrientation.Portrait => 90f,
                ScreenOrientation.LandscapeLeft => 180f,
                ScreenOrientation.LandscapeRight => 0f,
                ScreenOrientation.PortraitUpsideDown => -90f,
                _ => 0f,
            };
            return Quaternion.Euler(0f, 0f, angle);
        }

        private Camera GetCamera()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            return _camera;
        }

        private void Diagnose(string reason)
        {
            if (Time.realtimeSinceStartup - _lastDiagnoseTime <= DiagnoseIntervalSeconds)
            {
                return;
            }

            _lastDiagnoseTime = Time.realtimeSinceStartup;
            Debug.LogWarning($"arengine: localisation frame skipped, {reason}");
        }

        private static long GetTimestampNanoseconds()
        {
            if (!AREngineSession.IsCreated)
                return 0;
            AREngineFrame frame = AREngineSession.CurrentFrame;
            return frame.isValid && frame.timestampNs > 0 ? frame.timestampNs : 0;
        }
    }
}
