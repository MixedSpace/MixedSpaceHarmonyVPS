/* XrLoc owns map storage, localization and frame encoding.
   Geographic conversion uses managed code. The existing optional mapping API
   remains available to legacy samples through their PosePlugin workspace. */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MixedSpace.XR;
using UnityEngine;

namespace MixedSpace
{
    /// <summary>
    /// Maps, localization and frame encoding backed by libxrloc.
    ///
    /// Geographic conversion uses the managed transform. The optional legacy
    /// mapping workspace retains its existing public contract.
    /// Map residency is guarded; callers serialize orientation-probing localization.
    /// </summary>
    public sealed class MixedSpaceXrLocCore :
        IMixedSpaceNativeCore,
        IMixedSpaceNativeRuntime,
        IMixedSpaceNativeMapStore,
        IMixedSpaceNativeFrameEncoder,
        IMixedSpaceNativeLocalizer
    {
        /// <summary>
        /// How many localization attempts may run the image-orientation sweep before
        /// the caller sees a plain attempt. The sweep is how the build finds which
        /// orientation convention this device/payload combination wants; once a
        /// candidate succeeds it is locked in and the sweep never runs again. More
        /// than one attempt also covers the startup window, where the first frames
        /// ARCore hands out are still settling.
        /// </summary>
        private int? m_LastLoggedSolver;
        private int? m_LastLoggedAngle;
        private int? m_LastLoggedPriorMapKey;
        private int? m_LastReportedFailureStatus;
        private long m_NextFailureReportTicks;

        private readonly object m_Gate = new object();
        private readonly HashSet<int> m_ResidentMaps = new HashSet<int>();
        private readonly string m_PositionPriorLogPath;

        public MixedSpaceXrLocCore() : this(null) { }

        internal MixedSpaceXrLocCore(string positionPriorLogPath)
        {
            m_PositionPriorLogPath = positionPriorLogPath;
        }

        public IMixedSpaceNativeRuntime Runtime => this;
        public IMixedSpaceNativeMapStore Maps => this;
        public IMixedSpaceNativeFrameEncoder FrameEncoder => this;
        public IMixedSpaceNativeLocalizer Localizer => this;
        // Geographic conversion no longer calls PosePlugin on the localization path.
        public IMixedSpaceNativeGeoTransform GeoTransform { get; } = new MixedSpaceGeographicTransform();
        // Retained for the existing optional mapping sample's public contract.
        #region Runtime

        public void AttachLogSink(IntPtr callback)
        {
            MixedSpaceXrLocNative.RegisterLogCallback(callback);
        }

        /// <summary>
        /// Reports the library version as major.minor.patch. Returns null when the
        /// payload is missing or rejected the call, so callers can log the native
        /// generation next to the container generation they are trying to load.
        /// </summary>
        internal static string DescribeVersion()
        {
            int status = MixedSpaceXrLocNative.GetVersion(out MixedSpaceXrLocVersion version);
            return status == (int)MixedSpaceXrLocStatus.Ok
                ? $"{version.major}.{version.minor}.{version.patch}"
                : null;
        }

        #endregion Runtime

        #region Map storage

        public bool Import(int mapKey, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (payload.Length == 0)
                return false;
            if (MixedSpaceMapDefinition.IsRetiredKey(mapKey) ||
                MixedSpaceMapDefinition.HasLegacyHeader(payload))
            {
                MixedSpaceLogger.LogError($"Map {mapKey} uses a retired ID or container. An XrLoc map is required.");
                return false;
            }

            lock (m_Gate)
            {
                if (m_ResidentMaps.Contains(mapKey))
                    return true;
            }

            int status = MixedSpaceXrLocNative.LoadMap(mapKey, payload, new UIntPtr((ulong)payload.Length));
            if (status != (int)MixedSpaceXrLocStatus.Ok)
            {
                MixedSpaceLogger.LogError(
                    $"xrloc refused map {mapKey}: status={status} ({DescribeStatus(status)}), bytes={payload.Length}.");
                return false;
            }

            lock (m_Gate)
                m_ResidentMaps.Add(mapKey);
            MixedSpaceLogger.Log($"Loaded map {mapKey} into xrloc");
            return true;
        }

        public void Evict(int mapKey)
        {
            lock (m_Gate)
            {
                if (!m_ResidentMaps.Contains(mapKey))
                    return;
            }

            MixedSpaceXrLocNative.FreeMap(mapKey);
            lock (m_Gate)
                m_ResidentMaps.Remove(mapKey);
        }

        public Vector3[] ReadSparsePoints(int mapKey)
        {
            int status = MixedSpaceXrLocNative.PointsGetCount(mapKey, out int count);
            if (status != (int)MixedSpaceXrLocStatus.Ok || count <= 0)
                return Array.Empty<Vector3>();

            var buffer = new MixedSpaceXrLocPoint3D[count];
            GCHandle handle = default;
            try
            {
                handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                status = MixedSpaceXrLocNative.PointsGet(
                    mapKey, handle.AddrOfPinnedObject(), count, out int written);
                if (status != (int)MixedSpaceXrLocStatus.Ok || written <= 0)
                    return Array.Empty<Vector3>();

                var points = new Vector3[written];
                for (int i = 0; i < written; i++)
                    points[i] = new Vector3(buffer[i].x, buffer[i].y, buffer[i].z);
                return points;
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }

        #endregion Map storage

        #region Frame encoding

        /// <summary>
        /// Encodes raw pixels into PNG. prepareForMatching is ignored: unlike
        /// icvCaptureImage, the xrloc encoder has no matching-preparation mode and
        /// always emits a standalone image.
        /// </summary>
        public byte[] Encode(
            byte[] pixels,
            int width,
            int height,
            int channels,
            bool prepareForMatching = false)
        {
            if (pixels == null)
                throw new ArgumentNullException(nameof(pixels));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

            var encoded = new byte[checked(width * height * channels + 8192)];
            GCHandle encodedHandle = default;
            GCHandle pixelsHandle = default;
            try
            {
                encodedHandle = GCHandle.Alloc(encoded, GCHandleType.Pinned);
                pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                int status = MixedSpaceXrLocNative.CaptureImage(
                    encodedHandle.AddrOfPinnedObject(),
                    encoded.Length,
                    pixelsHandle.AddrOfPinnedObject(),
                    width,
                    height,
                    channels,
                    out int captureSize);
                if (status != (int)MixedSpaceXrLocStatus.Ok ||
                    captureSize <= 0 ||
                    captureSize > encoded.Length)
                {
                    return Array.Empty<byte>();
                }

                Array.Resize(ref encoded, captureSize);
                return encoded;
            }
            finally
            {
                if (pixelsHandle.IsAllocated)
                    pixelsHandle.Free();
                if (encodedHandle.IsAllocated)
                    encodedHandle.Free();
            }
        }

        #endregion Frame encoding

        #region Localization

        public MixedSpaceLocalizationObservation Resolve(
            MixedSpaceCaptureProfile profile,
            IntPtr pixelBuffer,
            MixedSpaceNativeLocalizationQuery query)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (query == null)
                throw new ArgumentNullException(nameof(query));
            if (pixelBuffer == IntPtr.Zero)
                return null;

            int[] mapIds = SnapshotResidentMaps();
            if (mapIds.Length == 0)
                return null;

            MixedSpaceXrLocCamera camera = BuildCamera(profile);
            float[] rotationPrior = BuildRotationPrior(profile);
            MixedSpaceXrLocPosePrior? positionPrior = BuildPositionPrior(mapIds, query);

            // 求解方式和角度由显式配置决定，定位失败不触发方向搜索。
            // Keep the configured solver and angle; localization misses never trigger orientation searches.
            int solver = SolverFor(query);
            int angle = ScreenRotationAngle(profile);
            int? priorMapKey = positionPrior?.mapId;
            if (m_LastLoggedSolver != solver || m_LastLoggedAngle != angle ||
                m_LastLoggedPriorMapKey != priorMapKey)
            {
                MixedSpaceLogger.Log(
                    $"[XRLOC-CONFIG] solver={solver} angle={angle} solverSource=RunPlan angleSource={(profile.NativeImageRotationAngle.HasValue ? "native-hint" : "display-rotation")} " +
                    $"rotationPrior=provided positionPriorMap={priorMapKey?.ToString() ?? "none"}",
                    MixedSpaceLogger.LoggingLevel.Verbose, nameof(MixedSpaceXrLocCore));
                m_LastLoggedSolver = solver;
                m_LastLoggedAngle = angle;
                m_LastLoggedPriorMapKey = priorMapKey;
            }

            if (!TryLocalize(
                    mapIds,
                    profile,
                    pixelBuffer,
                    camera,
                    rotationPrior,
                    solver,
                    angle,
                    positionPrior,
                    out MixedSpaceXrLocPose pose,
                    out int status,
                    out long elapsedMs))
            {
                // Ok with no pose is the ordinary miss: the call ran to completion and
                // found nothing in frame. Writing that once per frame buries the log,
                // and the localization service already reports a continuing run of
                // misses at a low rate, so only an unexpected status earns a line here.
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (m_LastReportedFailureStatus != status || now >= m_NextFailureReportTicks)
                {
                    m_LastReportedFailureStatus = status;
                    m_NextFailureReportTicks = now + 10L * System.Diagnostics.Stopwatch.Frequency;
                    MixedSpaceLogger.LogWarning(
                        $"[XRLOC-MISS] status={status} ({DescribeStatus(status)}) success={pose.success} " +
                        $"maps=[{DescribeResidentMaps(mapIds)}] solver={solver} angle={angle} " +
                        $"image={profile.PixelWidth}x{profile.PixelHeight}x{profile.ChannelCount} " +
                        $"intrinsics=({camera.fx},{camera.fy},{camera.cx},{camera.cy}) elapsedMs={elapsedMs}.");
                }

                return null;
            }

            m_LastReportedFailureStatus = null;
            return ToObservation(mapIds, pose);
        }

        /// <summary>
        /// One native localization call with an explicit image orientation.
        /// </summary>
        private bool TryLocalize(
            int[] mapIds,
            MixedSpaceCaptureProfile profile,
            IntPtr pixelBuffer,
            MixedSpaceXrLocCamera camera,
            float[] rotationPrior,
            int solver,
            int rotationAngle,
            MixedSpaceXrLocPosePrior? positionPrior,
            out MixedSpaceXrLocPose pose,
            out int status,
            out long elapsedMs)
        {
            var image = new MixedSpaceXrLocImage
            {
                data = pixelBuffer,
                width = profile.PixelWidth,
                height = profile.PixelHeight,
                // 0 means the rows are contiguous, which is how the capture buffer is laid out.
                stride = 0,
                format = ToImageFormat(profile.ChannelCount),
                rotationAngle = rotationAngle
            };

            MixedSpaceXrLocCamera localCamera = camera;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (positionPrior is MixedSpaceXrLocPosePrior nativePrior)
            {
                status = MixedSpaceXrLocNative.LocalizeWithPrior(
                    mapIds, mapIds.Length, ref image, ref localCamera, solver, rotationPrior, ref nativePrior, out pose);
            }
            else
            {
            status = MixedSpaceXrLocNative.Localize(
                    mapIds, mapIds.Length, ref image, ref localCamera, solver, rotationPrior, out pose);
            }
            elapsedMs = timer.ElapsedMilliseconds;

            if (m_PositionPriorLogPath != null && positionPrior is MixedSpaceXrLocPosePrior loggedPrior)
            {
                MixedSpaceLocalizationJournal.AppendPositionPriorLog(
                    m_PositionPriorLogPath,
                    profile.CapturedAtNanoseconds,
                    mapIds,
                    solver,
                    rotationAngle,
                    rotationPrior,
                    loggedPrior,
                    status,
                    pose,
                    elapsedMs);
            }

            return status == (int)MixedSpaceXrLocStatus.Ok && pose.success != 0;
        }

        /// <summary>
        /// Tries every orientation convention the payload may want and locks in the
        /// first that produces a pose.
        ///
        /// The C header claims a non-null rotation prior makes rotation_angle
        /// redundant, but the shipped payload disagrees: XrLocLocalize reads
        /// solver_type and picks the angle source from it alone - solver 1 derives
        /// the image roll from the prior quaternion, every other value falls back to
        /// image.rotation_angle and never looks at the prior. A caller that passes
        /// solver 0 with rotation_angle 0 therefore pins the image to an arbitrary
        /// quarter turn and feature matching can never converge, which is exactly the
        /// "Ok but success == 0" signature. Rather than guess which quarter the
        /// payload wants for this device, ask it once.
        /// </summary>
        private static MixedSpaceXrLocPosePrior? BuildPositionPrior(
            int[] mapIds,
            MixedSpaceNativeLocalizationQuery query)
        {
            if (!query.HasPrior)
                return null;

            int mapKey = query.PriorMapKey ?? (mapIds.Length == 1 ? mapIds[0] : 0);
            Vector3 position = query.PriorPosition;
            if (mapKey <= 0 || Array.IndexOf(mapIds, mapKey) < 0 ||
                !IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
                return null;

            return new MixedSpaceXrLocPosePrior
            {
                mapId = mapKey,
                x = position.x,
                y = position.y,
                z = position.z
            };
            }

            // Second sample of the same lease: identical bytes mean the buffer is a
            // frozen snapshot rather than a live frame.
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static int SolverFor(MixedSpaceNativeLocalizationQuery query) =>
            query != null && query.Solver == MixedSpaceSolverType.Lean ? 1 : 0;

        /// <summary>
        /// "id:points" for each resident map, so a probe that matches nothing can be
        /// told apart from a probe running against an empty or wrong map.
        /// </summary>
        private static string DescribeResidentMaps(int[] mapIds)
        {
            var parts = new string[mapIds.Length];
            for (int i = 0; i < mapIds.Length; i++)
            {
                int status = MixedSpaceXrLocNative.PointsGetCount(mapIds[i], out int count);
                parts[i] = status == (int)MixedSpaceXrLocStatus.Ok
                    ? mapIds[i] + ":" + count
                    : mapIds[i] + ":err" + status;
            }

            return string.Join(",", parts);
        }

        /// <summary>
        /// Writes the exact bytes and parameters handed to the payload for one frame
        /// so a hopeless match can be replayed off-device instead of guessed at, and
        /// logs the brightness histogram of that same buffer - an all-black or
        /// single-valued frame is the one failure mode an orientation sweep cannot
        /// expose. Sampling the same lease twice, once before and once after the
        /// sweep, also shows whether the buffer is a live camera frame or a frozen
        /// snapshot. Diagnostic only: it swallows every failure.
        /// </summary>
        private static int ScreenRotationAngle(MixedSpaceCaptureProfile profile)
        {
            if (profile.NativeImageRotationAngle.HasValue)
                return profile.NativeImageRotationAngle.Value;
            int counterClockwise = Mathf.RoundToInt(profile.DisplayRotation.eulerAngles.z) % 360;
            if (counterClockwise < 0)
                counterClockwise += 360;
            return (360 - counterClockwise) % 360;
        }

        private static MixedSpaceLocalizationObservation ToObservation(
            int[] mapIds,
            MixedSpaceXrLocPose pose)
        {
            int mapKey = pose.mapId;
            if (Array.IndexOf(mapIds, mapKey) < 0)
            {
                // The C header does not promise that map_id is written on every
                // success path, so fall back when a single map makes it unambiguous.
                if (mapIds.Length != 1)
                    return null;
                mapKey = mapIds[0];
            }

            if (mapKey <= 0)
                return null;

            return new MixedSpaceLocalizationObservation(
                mapKey,
                new Vector3((float)pose.px, (float)pose.py, (float)pose.pz),
                ToQuaternion(pose),
                pose.confidence);
        }

        private int[] SnapshotResidentMaps()
        {
            lock (m_Gate)
            {
                var ids = new int[m_ResidentMaps.Count];
                m_ResidentMaps.CopyTo(ids);
                // Deterministic order keeps a multi-map search reproducible.
                Array.Sort(ids);
                return ids;
            }
        }

        private static MixedSpaceXrLocCamera BuildCamera(MixedSpaceCaptureProfile profile)
        {
            Vector4 intrinsics = profile.CameraIntrinsics;
            IReadOnlyList<double> distortion = profile.LensDistortion;
            return new MixedSpaceXrLocCamera
            {
                fx = intrinsics.x,
                fy = intrinsics.y,
                cx = intrinsics.z,
                cy = intrinsics.w,
                k1 = DistortionCoefficient(distortion, 0),
                k2 = DistortionCoefficient(distortion, 1),
                p1 = DistortionCoefficient(distortion, 2),
                p2 = DistortionCoefficient(distortion, 3)
            };
        }

        private static double DistortionCoefficient(IReadOnlyList<double> values, int index) =>
            values != null && index < values.Count ? values[index] : 0d;

        /// <summary>
        /// Same orientation the PosePlugin path hands to icvLocalize: the tracker and
        /// display rotations composed, then reflected into the native handedness and
        /// serialized XYZW as the C API expects.
        /// </summary>
        private static float[] BuildRotationPrior(MixedSpaceCaptureProfile profile)
        {
            Quaternion orientation = MixedSpaceCoordinateFrame.ReflectXAxis(
                profile.TrackerRotation * profile.DisplayRotation);
            return MixedSpaceCoordinateFrame.CopyQuaternionComponents(orientation);
        }

        /// <summary>
        /// Derives the layout from the real channel count rather than the semantic
        /// pixel format, because the native side needs the byte layout to size rows.
        /// </summary>
        private static MixedSpaceXrLocImageFormat ToImageFormat(int channelCount)
        {
            switch (channelCount)
            {
                case 4:
                    return MixedSpaceXrLocImageFormat.Rgba;
                case 3:
                    return MixedSpaceXrLocImageFormat.Rgb;
                default:
                    return MixedSpaceXrLocImageFormat.Gray;
            }
        }

        /// <summary>
        /// Builds a Unity rotation from the row-major camera-to-world matrix. The
        /// translation column is excluded so the matrix stays a pure rotation, which
        /// is what Quaternion extraction requires.
        /// </summary>
        private static Quaternion ToQuaternion(MixedSpaceXrLocPose pose)
        {
            var matrix = new Matrix4x4
            {
                m00 = (float)pose.r00,
                m01 = (float)pose.r01,
                m02 = (float)pose.r02,
                m03 = 0f,
                m10 = (float)pose.r10,
                m11 = (float)pose.r11,
                m12 = (float)pose.r12,
                m13 = 0f,
                m20 = (float)pose.r20,
                m21 = (float)pose.r21,
                m22 = (float)pose.r22,
                m23 = 0f,
                m30 = 0f,
                m31 = 0f,
                m32 = 0f,
                m33 = 1f
            };
            return matrix.rotation;
        }

        internal static string DescribeStatus(int status)
        {
            var code = (MixedSpaceXrLocStatus)status;
            return Enum.IsDefined(typeof(MixedSpaceXrLocStatus), code)
                ? code.ToString()
                : "Unknown";
        }

        #endregion Localization
    }
}

