using System;
using System.IO;
using UnityEngine;

namespace MixedSpace.XR
{
    /// <summary>Optional configuration port: diagnostics do not alter the platform lifecycle ABI.</summary>
    public interface IMixedSpaceCaptureDiagnostics
    {
        void SetWritePoseLogs(bool enabled);
    }

    /// <summary>Shared capture journal for optional adapters. Uses the existing JSON-lines schema.</summary>
    internal static class MixedSpaceCaptureJournal
    {
        [Serializable] private sealed class PoseData
        {
            public float[] pose;
            public long timestamp;
            public int width;
            public int height;
        }
        [Serializable] private sealed class Entry
        {
            public long timestamp_ms;
            public PoseData capturePose;
        }
        public static void Append(MixedSpaceCaptureProfile profile)
        {
            try
            {
                Matrix4x4 pose = Matrix4x4.TRS(profile.TrackerPosition, profile.TrackerRotation, Vector3.one);
                var matrix = new float[16];
                for (int i = 0; i < 16; i++) matrix[i] = pose[i];
                var entry = new Entry { timestamp_ms = profile.CapturedAtNanoseconds / 1000000,
                    capturePose = new PoseData { pose = matrix, timestamp = profile.CapturedAtNanoseconds,
                        width = profile.PixelWidth, height = profile.PixelHeight } };
                File.AppendAllText(MixedSpacePoseLogFiles.GetPath("camera_capture_poses.json"),
                    JsonUtility.ToJson(entry) + Environment.NewLine);
            }
            catch (Exception error)
            {
                MixedSpaceLogger.LogError("Capture journal write failed (" + error.GetType().Name + ").");
            }
        }
    }
}
