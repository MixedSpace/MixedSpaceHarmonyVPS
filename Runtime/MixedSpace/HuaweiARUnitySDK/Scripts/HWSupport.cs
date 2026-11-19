using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using HuaweiARUnitySDK;
using HuaweiARInternal;

namespace MixedSpace.XR
{
    public class HWSupport : MonoBehaviour, IMixedSpacePlatformAdapter
    {
        private const float DefaultNear = 0.01f;
        private const float DefaultFar = 1000f;

        private bool m_ConfigDone = false;
        // [LocDiag] 取图失败诊断日志节流时间戳(排查“不发送定位”用，定位后可移除)
        private static float s_lastAcqDiag = -999f;
        private Task<IMixedSpacePlatformFrame> m_CurrentCameraDataTask;
        private Camera m_Camera;

        [SerializeField]
        private MixedSpacePixelFormat m_CameraDataFormat = MixedSpacePixelFormat.Luminance8;

        public MixedSpacePixelFormat CameraDataFormat => m_CameraDataFormat;

        public bool IsSessionReady()
        {
            return ARSessionManager.Instance != null &&
                   ARSessionManager.Instance.IsCreated();
        }

        public Texture2D GetCameraTexture()
        {
            if (!ARFrame.TextureIsAvailable())
            {
                return null;
            }

            return ARFrame.CameraTexture;
        }

        public bool TryGetCameraTexture(out Texture2D texture)
        {
            texture = GetCameraTexture();
            return texture != null;
        }

        public bool TryGetCameraIntrinsics(out ARCameraIntrinsics intrinsics)
        {
            intrinsics = default(ARCameraIntrinsics);

            if (!IsSessionReady())
            {
                return false;
            }

            IntPtr cameraHandle = ARSessionManager.Instance.m_ndkSession.FrameAdapter.AcquireCameraHandle();
            if (cameraHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                intrinsics = ARSessionManager.Instance.m_ndkSession.CameraAdapter.GetImageIntrinsics(cameraHandle);
                return true;
            }
            finally
            {
                ARSessionManager.Instance.m_ndkSession.CameraAdapter.Release(cameraHandle);
            }
        }

        public bool TryGetCameraPose(out Pose pose)
        {
            pose = Pose.identity;

            if (!IsSessionReady())
            {
                return false;
            }

            pose = ARFrame.GetPose();
            return true;
        }

        public bool TryGetProjectionMatrix(float nearClipPlane, float farClipPlane, out Matrix4x4 projectionMatrix)
        {
            projectionMatrix = Matrix4x4.identity;

            if (!IsSessionReady())
            {
                return false;
            }

            projectionMatrix = ARSession.GetProjectionMatrix(nearClipPlane, farClipPlane);
            return true;
        }

        public Task<bool> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_ConfigDone = true;
            return Task.FromResult(true);
        }

        public Task<IMixedSpacePlatformFrame> CaptureFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_ConfigDone || !isActiveAndEnabled)
                return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                    new MixedSpaceTrackingSnapshot(0), null));
            m_CurrentCameraDataTask = GetCameraData(cancellationToken);
            return m_CurrentCameraDataTask;
        }

        public MixedSpacePlatformPoseSample SamplePose()
        {
            Camera cam = GetCamera();
            return new MixedSpacePlatformPoseSample
            {
                Position = cam != null ? cam.transform.position : Vector3.zero,
                Rotation = cam != null ? cam.transform.rotation : Quaternion.identity,
                TimestampNanoseconds = GetTimestampNanoseconds(),
                IsTracking = cam != null && IsSessionReady()
            };
        }

        public async Task ShutdownAsync()
        {
            m_ConfigDone = false;
            if (m_CurrentCameraDataTask != null)
                await m_CurrentCameraDataTask;
        }

        private Task<IMixedSpacePlatformFrame> GetCameraData(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Camera cam = GetCamera();
            if (!IsSessionReady() || cam == null ||
                !TryAcquireCameraImageY(out byte[] imageBytes, out int imgWidth, out int imgHeight) ||
                !GetIntrinsicsForImage(imgWidth, imgHeight, out Vector4 intrinsics))
                return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                    new MixedSpaceTrackingSnapshot(0), null));

            cancellationToken.ThrowIfCancellationRequested();
            var profile = new MixedSpaceCaptureProfile(
                imgWidth, imgHeight, 1, MixedSpacePixelFormat.Luminance8,
                intrinsics, cam.transform.position, cam.transform.rotation,
                GetTimestampNanoseconds(), GetOrientation());
            // The borrowed AREngine image has already been released by the CPU copy.
            // The new capture owns the pinned buffer until its final pixel lease ends.
            var capture = new MixedSpaceCameraCapture(profile, new MixedSpaceManagedPixelSource(imageBytes));
            return Task.FromResult<IMixedSpacePlatformFrame>(new MixedSpacePlatformFrame(
                new MixedSpaceTrackingSnapshot(1), capture));
        }

        // 从华为 AR NDK 的 CPU 侧相机图像(YUV_420_888)读取 Y 平面(灰度)数据。
        // 这是 HarmonyOS 上唯一可靠的取图方式：native texture 不能 GetPixels32/GetRawTextureData。
        // 失败时返回 false(而非抛异常)，让 session 跳过当前帧、下一帧继续，避免整个会话被中断。
        private bool TryAcquireCameraImageY(out byte[] yBytes, out int imgWidth, out int imgHeight)
        {
            yBytes = null;
            imgWidth = 0;
            imgHeight = 0;

            if (!IsSessionReady())
            {
                // [LocDiag] 门槛2细分：会话未就绪(ARSessionManager.IsCreated=false)，取不到相机图→Success=false→不发送定位。
                if (Time.realtimeSinceStartup - s_lastAcqDiag > 2f)
                {
                    s_lastAcqDiag = Time.realtimeSinceStartup;
                    UnityEngine.Debug.LogWarning("[LocDiag] 门槛2细分-取图失败: 会话未就绪(IsCreated=false，AR会话没创建/已STOPPED/没到RUNNING)");
                }
                return false;
            }

            NDKSession ndk = ARSessionManager.Instance.m_ndkSession;
            IntPtr imageHandle = IntPtr.Zero;
            try
            {
                imageHandle = ndk.FrameAdapter.AcquireCameraImage();
                if (imageHandle == IntPtr.Zero)
                {
                    // [LocDiag] 门槛2细分：native AcquireCameraImage 返回 Zero，多为 AREngine 相机帧不可用(hal data queue empty/数据饥饿)。
                    if (Time.realtimeSinceStartup - s_lastAcqDiag > 2f)
                    {
                        s_lastAcqDiag = Time.realtimeSinceStartup;
                        UnityEngine.Debug.LogWarning("[LocDiag] 门槛2细分-取图失败: AcquireCameraImage返回Zero(AREngine无可用相机帧,数据饥饿)");
                    }
                    return false;
                }

                imgWidth = ndk.ImageAdapter.GetWidth(imageHandle);
                imgHeight = ndk.ImageAdapter.GetHeight(imageHandle);
                if (imgWidth <= 0 || imgHeight <= 0)
                {
                    return false;
                }

                // Plane 0 = Y(亮度)，即灰度数据，正是 SingleChannel(R8) 所需。
                int rowStride = ndk.ImageAdapter.GetPlaneRowStride(imageHandle, 0);
                IntPtr planeData = ndk.ImageAdapter.GetPlaneData(imageHandle, 0, out int dataLength);
                if (planeData == IntPtr.Zero || dataLength < imgWidth * imgHeight)
                {
                    return false;
                }

                // 字节序必须与 Android 及本类 GetTextureBytes(GetPixels32) 的约定一致：row 0 = 底行。
                // 传感器 Y 平面是顶行在前，若原序拷贝上传图会上下翻转——与传感器 90° 旋转叠加后
                // 恰等价于“顺时针 90° + 水平镜像”的错姿，且图像与内参几何不一致导致服务端位姿解算错误。
                yBytes = new byte[imgWidth * imgHeight];
                for (int row = 0; row < imgHeight; row++)
                {
                    // 输出第 row 行(自底向上)对应传感器第 (imgHeight-1-row) 行；
                    // rowStride > imgWidth 时同时跳过每行末尾的对齐 padding。
                    Marshal.Copy(IntPtr.Add(planeData, (imgHeight - 1 - row) * rowStride), yBytes, row * imgWidth, imgWidth);
                }

                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[HWSupport] AcquireCameraImage(Y) 失败: " + e.Message);
                return false;
            }
            finally
            {
                if (imageHandle != IntPtr.Zero)
                {
                    ndk.ImageAdapter.Release(imageHandle);
                }
            }
        }

        public bool GetIntrinsics(out Vector4 intrinsics)
        {
            intrinsics = Vector4.zero;

            if (!IsSessionReady())
            {
                return false;
            }

            IntPtr cameraHandle = ARSessionManager.Instance.m_ndkSession.FrameAdapter.AcquireCameraHandle();
            if (cameraHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                ARCameraIntrinsics hwIntrinsics = ARSessionManager.Instance.m_ndkSession.CameraAdapter.GetImageIntrinsics(cameraHandle);
                intrinsics = new Vector4(hwIntrinsics.fx, hwIntrinsics.fy, hwIntrinsics.cx, hwIntrinsics.cy);
                return true;
            }
            finally
            {
                ARSessionManager.Instance.m_ndkSession.CameraAdapter.Release(cameraHandle);
            }
        }

        // 把内参对齐到实际提供图像的方向：部分 HarmonyOS 设备上 HwArCamera_getImageIntrinsics
        // 返回的是显示方向内参(width/height 与 CPU 传感器图像转置)，需按 90° 旋转关系交换 fx/fy、cx/cy，
        // 否则图像与内参几何不一致(“内参反的”)，服务端位姿解算必然错误。
        // 转置判定双保险：①内参自带 width/height 与图像尺寸转置；②主点 cx 更接近图像高度中心(正常应接近宽度中心)。
        private bool GetIntrinsicsForImage(int imgWidth, int imgHeight, out Vector4 intrinsics)
        {
            intrinsics = Vector4.zero;

            if (!IsSessionReady())
            {
                return false;
            }

            IntPtr cameraHandle = ARSessionManager.Instance.m_ndkSession.FrameAdapter.AcquireCameraHandle();
            if (cameraHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                ARCameraIntrinsics hw = ARSessionManager.Instance.m_ndkSession.CameraAdapter.GetImageIntrinsics(cameraHandle);
                bool transposed = (hw.width == imgHeight && hw.height == imgWidth) ||
                                  Mathf.Abs(hw.cx - imgWidth * 0.5f) > Mathf.Abs(hw.cx - imgHeight * 0.5f);
                if (transposed)
                {
                    // 内参处于转置(显示)方向：按 90° 旋转关系交换，对齐到传感器方向图像。
                    intrinsics = new Vector4(hw.fy, hw.fx, hw.cy, hw.cx);
                    UnityEngine.Debug.Log($"[HWSupport] 内参为转置方向，已交换对齐图像: raw={hw} -> fx={intrinsics.x}, fy={intrinsics.y}, cx={intrinsics.z}, cy={intrinsics.w}, image={imgWidth}x{imgHeight}");
                }
                else
                {
                    intrinsics = new Vector4(hw.fx, hw.fy, hw.cx, hw.cy);
                    if (hw.width != imgWidth || hw.height != imgHeight)
                    {
                        UnityEngine.Debug.LogWarning($"[HWSupport] 内参尺寸 {hw.width}x{hw.height} 与图像 {imgWidth}x{imgHeight} 不一致");
                    }
                }
                return true;
            }
            finally
            {
                ARSessionManager.Instance.m_ndkSession.CameraAdapter.Release(cameraHandle);
            }
        }

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
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
            return m_Camera;
        }

        private static byte[] GetTextureBytes(Texture2D tex, MixedSpacePixelFormat format)
        {
            if (tex == null)
            {
                return null;
            }

            if (format == MixedSpacePixelFormat.Luminance8)
            {
                Color32[] pixels = tex.GetPixels32();
                byte[] bytes = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    bytes[i] = pixels[i].r;
                }
                return bytes;
            }

            return tex.GetRawTextureData<byte>().ToArray();
        }

        private static long GetTimestampNanoseconds()
        {
            return DateTime.UtcNow.Ticks * 100;
        }
    }
}
