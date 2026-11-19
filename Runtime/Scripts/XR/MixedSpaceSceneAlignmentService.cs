using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MixedSpace.XR
{
    /// <summary>
    /// Immutable solver input and solved-state metadata. No camera owner, pixel
    /// lease, map binding or world-output channel can be retained by a read model
    /// or application view. Pose corrections belong to an alignment strategy.
    /// </summary>
    public sealed class MixedSpaceAlignmentRequest
    {
        private readonly double[] m_MapToEcef;

        public int MapKey { get; }
        public MixedSpaceMapPlacement Placement { get; }
        public MixedSpaceCaptureProfile CaptureProfile { get; }
        public MixedSpaceLocalizationOutcome Outcome { get; }

        public MixedSpaceAlignmentRequest(
            int mapKey,
            MixedSpaceMapPlacement placement,
            MixedSpaceCaptureProfile captureProfile,
            MixedSpaceLocalizationOutcome outcome,
            IEnumerable<double> mapToEcef = null)
        {
            if (mapKey <= 0)
                throw new ArgumentOutOfRangeException(nameof(mapKey));
            if (outcome == null)
                throw new ArgumentNullException(nameof(outcome));
            if (!outcome.Success || outcome.Observation?.MapKey != mapKey)
                throw new ArgumentException(
                    "Alignment requires a successful observation for the declared map.",
                    nameof(outcome));

            MapKey = mapKey;
            Placement = placement ?? throw new ArgumentNullException(nameof(placement));
            CaptureProfile = captureProfile ?? throw new ArgumentNullException(nameof(captureProfile));
            Outcome = outcome;
            m_MapToEcef = mapToEcef?.ToArray();
            if (m_MapToEcef != null && m_MapToEcef.Length != 13)
                throw new ArgumentException(
                    "A map-to-ECEF transform contains translation, a 3x3 rotation and scale (13 values).",
                    nameof(mapToEcef));
        }

        /// <summary>
        /// Returns an isolated copy for geographic consumers, or null when the
        /// caller does not supply a geographic transform. Native consumers may
        /// mutate their copy without changing a cached alignment request.
        /// </summary>
        public double[] CopyMapToEcef() => (double[])m_MapToEcef?.Clone();

        /// <summary>Corrects a pose observation without changing the map identity.</summary>
        public MixedSpaceAlignmentRequest WithOutcome(MixedSpaceLocalizationOutcome outcome)
        {
            return new MixedSpaceAlignmentRequest(
                MapKey, Placement, CaptureProfile, outcome, m_MapToEcef);
        }

        /// <summary>Replaces capture metadata without acquiring or owning pixels.</summary>
        public MixedSpaceAlignmentRequest WithCaptureProfile(MixedSpaceCaptureProfile captureProfile)
        {
            return new MixedSpaceAlignmentRequest(
                MapKey, Placement, captureProfile, Outcome, m_MapToEcef);
        }
    }

    /// <summary>
    /// Immutable result of alignment calculation. This is the only value passed
    /// from localization into the world-output layer.
    /// </summary>
    public sealed class MixedSpaceAlignmentSolution
    {
        public Matrix4x4 WorldPose { get; }
        public Matrix4x4 TrackerPose { get; }
        public Matrix4x4 MapPose { get; }
        public long TimestampNanoseconds { get; }
        public int Confidence { get; }
        public Vector3 CapturePosition { get; }
        public Quaternion CaptureRotation { get; }
        public int MapKey { get; }
        public MixedSpaceMapPlacement Placement { get; }
        public Vector3 LocalizedPosition { get; }
        public Quaternion LocalizedRotation { get; }
        public Quaternion CameraOrientation { get; }

        public MixedSpaceAlignmentSolution(
            Matrix4x4 worldPose,
            Matrix4x4 trackerPose,
            Matrix4x4 mapPose,
            long timestampNanoseconds,
            int confidence,
            Vector3 capturePosition,
            Quaternion captureRotation,
            int mapKey,
            MixedSpaceMapPlacement placement,
            Vector3 localizedPosition,
            Quaternion localizedRotation,
            Quaternion cameraOrientation)
        {
            WorldPose = worldPose;
            TrackerPose = trackerPose;
            MapPose = mapPose;
            TimestampNanoseconds = timestampNanoseconds;
            Confidence = confidence;
            CapturePosition = capturePosition;
            CaptureRotation = captureRotation;
            MapKey = mapKey;
            Placement = placement;
            LocalizedPosition = localizedPosition;
            LocalizedRotation = localizedRotation;
            CameraOrientation = cameraOrientation;
        }

        public MixedSpaceAlignmentSolution WithWorldPose(Matrix4x4 worldPose)
        {
            return new MixedSpaceAlignmentSolution(
                worldPose,
                TrackerPose,
                MapPose,
                TimestampNanoseconds,
                Confidence,
                CapturePosition,
                CaptureRotation,
                MapKey,
                Placement,
                LocalizedPosition,
                LocalizedRotation,
                CameraOrientation);
        }
    }

    /// <summary>
    /// Stateless alignment solver. Scene output is deliberately handled by a
    /// separate channel so this class can be tested without a GameObject.
    /// </summary>
    public sealed class MixedSpaceAlignmentSolver : IMixedSpaceAlignmentSolver
    {
        public MixedSpaceAlignmentSolution Solve(MixedSpaceAlignmentRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            MixedSpaceLocalizationObservation observation =
                request.Outcome.Observation ??
                throw new ArgumentException(
                    "A successful localization observation is required.",
                    nameof(request));
            Vector3 localizedPosition = observation.Position;
            MixedSpaceCaptureProfile profile = request.CaptureProfile;
            Quaternion localizedRotation = observation.Rotation * profile.DisplayRotation;

            localizedPosition = MixedSpaceCoordinateFrame.ReflectXAxis(localizedPosition);
            localizedRotation = MixedSpaceCoordinateFrame.ReflectXAxis(localizedRotation);

            MixedSpaceMapPlacement placement = request.Placement;
            Matrix4x4 placementPose = Matrix4x4.TRS(
                placement.Translation,
                placement.Orientation,
                Vector3.one);
            Vector3 scaledPosition = Vector3.Scale(localizedPosition, placement.Scale);
            Matrix4x4 mapPose = placementPose * Matrix4x4.TRS(
                scaledPosition,
                localizedRotation,
                Vector3.one);

            Matrix4x4 trackerPose = Matrix4x4.TRS(
                profile.TrackerPosition,
                profile.TrackerRotation,
                Vector3.one);
            Matrix4x4 worldPose = trackerPose * mapPose.inverse;

            return new MixedSpaceAlignmentSolution(
                worldPose,
                trackerPose,
                mapPose,
                profile.CapturedAtNanoseconds,
                observation.Confidence,
                profile.TrackerPosition,
                profile.TrackerRotation,
                request.MapKey,
                request.Placement,
                observation.Position,
                observation.Rotation,
                profile.DisplayRotation);
        }
    }

}
