using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace MixedSpace.XR
{
    /// <summary>
    /// Platform-independent pixel layouts understood by localization and mapping.
    /// A platform adapter is responsible for converting its native camera format
    /// into one of these layouts before publishing a capture.
    /// </summary>
    public enum MixedSpacePixelFormat
    {
        Luminance8,
        Rgb24
    }

    /// <summary>
    /// Immutable camera and tracker facts recorded at the instant a frame was
    /// captured. Grouping them into one value prevents partially initialized
    /// frames from entering the localization pipeline.
    /// </summary>
    public sealed class MixedSpaceCaptureProfile
    {
        private readonly ReadOnlyCollection<double> m_LensDistortion;

        public MixedSpaceCaptureProfile(
            int pixelWidth,
            int pixelHeight,
            int channelCount,
            MixedSpacePixelFormat pixelFormat,
            Vector4 cameraIntrinsics,
            Vector3 trackerPosition,
            Quaternion trackerRotation,
            long capturedAtNanoseconds,
            Quaternion displayRotation,
            IEnumerable<double> lensDistortion = null,
            int? nativeImageRotationAngle = null)
        {
            if (pixelWidth < 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
            if (pixelHeight < 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));
            if (channelCount < 0) throw new ArgumentOutOfRangeException(nameof(channelCount));

            if (nativeImageRotationAngle.HasValue &&
                nativeImageRotationAngle != 0 && nativeImageRotationAngle != 90 &&
                nativeImageRotationAngle != 180 && nativeImageRotationAngle != 270)
                throw new ArgumentOutOfRangeException(nameof(nativeImageRotationAngle));

            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            ChannelCount = channelCount;
            PixelFormat = pixelFormat;
            CameraIntrinsics = cameraIntrinsics;
            TrackerPosition = trackerPosition;
            TrackerRotation = trackerRotation;
            CapturedAtNanoseconds = capturedAtNanoseconds;
            DisplayRotation = displayRotation;
            NativeImageRotationAngle = nativeImageRotationAngle;
            m_LensDistortion = new ReadOnlyCollection<double>(
                (lensDistortion ?? Enumerable.Empty<double>()).ToArray());
        }

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public int ChannelCount { get; }
        public MixedSpacePixelFormat PixelFormat { get; }
        public Vector4 CameraIntrinsics { get; }
        public Vector3 TrackerPosition { get; }
        public Quaternion TrackerRotation { get; }
        public long CapturedAtNanoseconds { get; }
        public Quaternion DisplayRotation { get; }
        public int? NativeImageRotationAngle { get; }
        public IReadOnlyList<double> LensDistortion => m_LensDistortion;
    }

    /// <summary>
    /// A platform-owned pixel resource. Implementations may wrap native camera
    /// handles or pinned managed memory, but never know about the capture that
    /// owns them.
    /// </summary>
    public interface IMixedSpacePixelSource : IDisposable
    {
        IntPtr Pointer { get; }
        byte[] CopyPixels();
    }

    /// <summary>
    /// Short-lived access to a capture's pixels. Disposing a lease only returns
    /// that lease; the last lease never decides who owns the camera frame.
    /// </summary>
    public interface IMixedSpacePixelLease : IDisposable
    {
        IntPtr Pointer { get; }
        byte[] CopyPixels();
    }

    public interface IMixedSpaceCameraCapture : IDisposable
    {
        MixedSpaceCaptureProfile Profile { get; }
        IMixedSpacePixelLease LeasePixels();
    }

    /// <summary>
    /// Owns one pixel source and defers its disposal while active leases exist.
    /// This is the only reference-counted object in the capture model; consumers
    /// cannot manipulate its count or make a child resource call back into it.
    /// </summary>
    public sealed class MixedSpaceCameraCapture : IMixedSpaceCameraCapture
    {
        private readonly object m_Gate = new object();
        private IMixedSpacePixelSource m_Source;
        private int m_ActiveLeases;
        private bool m_DisposeRequested;

        public MixedSpaceCameraCapture(
            MixedSpaceCaptureProfile profile,
            IMixedSpacePixelSource source)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            m_Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public MixedSpaceCaptureProfile Profile { get; }

        public IMixedSpacePixelLease LeasePixels()
        {
            lock (m_Gate)
            {
                if (m_DisposeRequested || m_Source == null)
                    throw new ObjectDisposedException(nameof(MixedSpaceCameraCapture));

                m_ActiveLeases++;
                return new PixelLease(m_Source, ReturnLease);
            }
        }

        public void Dispose()
        {
            IMixedSpacePixelSource sourceToDispose = null;
            lock (m_Gate)
            {
                if (m_DisposeRequested)
                    return;

                m_DisposeRequested = true;
                if (m_ActiveLeases == 0)
                {
                    sourceToDispose = m_Source;
                    m_Source = null;
                }
            }

            sourceToDispose?.Dispose();
        }

        private void ReturnLease()
        {
            IMixedSpacePixelSource sourceToDispose = null;
            lock (m_Gate)
            {
                if (m_ActiveLeases == 0)
                    return;

                m_ActiveLeases--;
                if (m_DisposeRequested && m_ActiveLeases == 0)
                {
                    sourceToDispose = m_Source;
                    m_Source = null;
                }
            }

            sourceToDispose?.Dispose();
        }

        private sealed class PixelLease : IMixedSpacePixelLease
        {
            private IMixedSpacePixelSource m_Source;
            private Action m_Return;

            public PixelLease(IMixedSpacePixelSource source, Action returnLease)
            {
                m_Source = source;
                m_Return = returnLease;
            }

            public IntPtr Pointer => GetSource().Pointer;

            public byte[] CopyPixels()
            {
                return GetSource().CopyPixels();
            }

            public void Dispose()
            {
                Action returnLease = Interlocked.Exchange(ref m_Return, null);
                if (returnLease == null)
                    return;

                Interlocked.Exchange(ref m_Source, null);
                returnLease();
            }

            private IMixedSpacePixelSource GetSource()
            {
                return Volatile.Read(ref m_Source) ??
                    throw new ObjectDisposedException(nameof(IMixedSpacePixelLease));
            }
        }
    }

    /// <summary>
    /// Pixel source for transformed or test data. The source pins its private
    /// buffer for native calls and returns copies to managed callers.
    /// </summary>
    public sealed class MixedSpaceManagedPixelSource : IMixedSpacePixelSource
    {
        private byte[] m_Pixels;
        private GCHandle m_PinnedPixels;
        private IntPtr m_Pointer;

        public MixedSpaceManagedPixelSource(byte[] pixels)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            m_Pixels = (byte[])pixels.Clone();
            m_PinnedPixels = GCHandle.Alloc(m_Pixels, GCHandleType.Pinned);
            m_Pointer = m_PinnedPixels.AddrOfPinnedObject();
        }

        public IntPtr Pointer
        {
            get
            {
                EnsureActive();
                return m_Pointer;
            }
        }

        public byte[] CopyPixels()
        {
            EnsureActive();
            return (byte[])m_Pixels.Clone();
        }

        public void Dispose()
        {
            if (m_Pixels == null)
                return;

            if (m_PinnedPixels.IsAllocated)
                m_PinnedPixels.Free();
            m_Pixels = null;
            m_Pointer = IntPtr.Zero;
        }

        private void EnsureActive()
        {
            if (m_Pixels == null)
                throw new ObjectDisposedException(nameof(MixedSpaceManagedPixelSource));
        }
    }
}
