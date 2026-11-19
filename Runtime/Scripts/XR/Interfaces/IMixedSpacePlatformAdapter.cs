using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MixedSpace.XR
{
    public interface IMixedSpacePlatformFrame
    {
        bool HasCapture { get; }
        MixedSpaceTrackingSnapshot Tracking { get; }
        IMixedSpaceCameraCapture Capture { get; }
    }

    /// <summary>
    /// Operating-system camera/tracking port. The core asks for capabilities in
    /// lifecycle terms rather than mirroring ARFoundation configuration methods.
    /// </summary>
    public interface IMixedSpacePlatformAdapter
    {
        Task<bool> StartAsync(CancellationToken cancellationToken);
        Task<IMixedSpacePlatformFrame> CaptureFrameAsync(
            CancellationToken cancellationToken);
        MixedSpacePlatformPoseSample SamplePose();
        Task ShutdownAsync();
    }
    
    /// <summary>
    /// 提供跟踪丢失时间的可选接口，供位姿融合判断历史何时失效。
    /// Optional source of tracking loss timestamps that invalidate pose fusion history.
    /// </summary>
    public interface IMixedSpaceTrackingLossSource
    {
        /// <summary>
        /// 最近一次观测到跟踪由可用变为丢失的时间，单位为纳秒，与定位图像使用同一时钟。
        /// 首次丢失前返回 0，恢复跟踪后保留该值，供融合通道检测短暂丢失。
        /// Last observed transition from tracking to lost, in nanoseconds on the localization capture clock.
        /// Zero until the first loss. The value persists after recovery so fusion can detect brief losses.
        /// </summary>
        long LastTrackingLossTimestampNanoseconds { get; }
    }

    #region Simple implementations
    
    /// <summary>
    /// Immutable platform result. Capture availability is derived from the
    /// presence of a capture instead of being maintained as a second flag.
    /// </summary>
    public sealed class MixedSpacePlatformFrame : IMixedSpacePlatformFrame
    {
        public MixedSpacePlatformFrame(
            MixedSpaceTrackingSnapshot tracking,
            IMixedSpaceCameraCapture capture)
        {
            Tracking = tracking;
            Capture = capture;
        }

        public bool HasCapture => Capture != null;
        public MixedSpaceTrackingSnapshot Tracking { get; }
        public IMixedSpaceCameraCapture Capture { get; }
    }

    /// <summary>
    /// 实时跟踪位姿时间戳来源。用于诊断，跨时钟同步由平台适配器负责。
    /// Origin of a live tracking timestamp. Diagnostic only; adapters own clock synchronization.
    /// </summary>
    public enum MixedSpaceTrackingTimestampSource
    {
        Unspecified,
        Device,
        DevicePredicted,
        HostMonotonic,
        HostUtc
    }

    /// <summary>
    /// 融合管线使用的实时跟踪位姿，由适配器转换到 Unity 坐标系。
    /// 图像采集对应的位姿另存于 Capture.Profile，同步精度由适配器决定，不应直接用本结构替代。
    /// Live fusion pose expressed in Unity coordinates. Capture.Profile contains the image-associated
    /// pose with adapter-specific synchronization; this structure must not replace it directly.
    /// </summary>
    public struct MixedSpacePlatformPoseSample
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public long TimestampNanoseconds { get; set; }
        /// <summary>
        /// 未提供来源的适配器使用 Unspecified。
        /// Adapters that do not identify their clock use Unspecified.
        /// </summary>
        public MixedSpaceTrackingTimestampSource TimestampSource { get; set; }
        /// <summary>
        /// 本次位姿是否可用于跟踪；为 false 时不应使用位置、旋转或时间戳参与融合。
        /// Whether this pose is usable for tracking; when false, do not feed its position, rotation or
        /// timestamp into fusion.
        /// </summary>
        public bool IsTracking { get; set; }
    }

    public readonly struct MixedSpaceTrackingSnapshot
    {
        public MixedSpaceTrackingSnapshot(int quality)
        {
            Quality = quality;
        }

        public int Quality { get; }
    }
    #endregion
}
