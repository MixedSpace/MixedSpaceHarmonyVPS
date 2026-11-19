// Hand-written managed types for libAREngineBridge.so.
//
// The two generated files keep the Huawei C API spelling because they transcribe
// it one-to-one. This file is written for Unity consumers instead, so records are
// decoded into Unity's Pose / Matrix4x4 / Bounds and follow this project's naming
// conventions.

using System;
using UnityEngine;

namespace AREngineBridge
{
    /// <summary>
    ///     decodes the fixed-stride float records that the TJAREX_* batched
    ///     getters hand back
    /// </summary>
    /// <remarks>
    ///     every record layout in tjarex.h puts the quaternion first with the
    ///     translation immediately after it, so a single reader serves all of
    ///     them; the caller passes the matching AREngineX offset
    /// </remarks>
    internal static class AREngineRecord
    {
        /// <summary>
        ///     reads a 7-float [qx, qy, qz, qw, tx, ty, tz] pose
        /// </summary>
        /// <param name="buffer">the record buffer</param>
        /// <param name="offset">index of the quaternion's first float</param>
        /// <returns>the pose in metres, right-handed, OpenGL camera convention</returns>
        internal static Pose ReadPose(float[] buffer, int offset)
        {
            return new Pose(
                new Vector3(buffer[offset + 4], buffer[offset + 5], buffer[offset + 6]),
                new Quaternion(buffer[offset], buffer[offset + 1], buffer[offset + 2], buffer[offset + 3]));
        }

        /// <summary>
        ///     writes a pose as 7 floats, [qx, qy, qz, qw, tx, ty, tz]
        /// </summary>
        /// <param name="pose">the pose in metres</param>
        /// <param name="buffer">the destination, which must have 7 free floats at offset</param>
        /// <param name="offset">index to start writing at</param>
        internal static void WritePose(Pose pose, float[] buffer, int offset)
        {
            buffer[offset] = pose.rotation.x;
            buffer[offset + 1] = pose.rotation.y;
            buffer[offset + 2] = pose.rotation.z;
            buffer[offset + 3] = pose.rotation.w;
            buffer[offset + 4] = pose.position.x;
            buffer[offset + 5] = pose.position.y;
            buffer[offset + 6] = pose.position.z;
        }

        /// <summary>
        ///     reads a 16-float column-major matrix
        /// </summary>
        /// <param name="buffer">the record buffer</param>
        /// <param name="offset">index of the matrix's first float</param>
        /// <returns>the matrix, ready to assign to Camera.projectionMatrix</returns>
        internal static Matrix4x4 ReadMatrix(float[] buffer, int offset)
        {
            // AREngine returns column-major OpenGL matrices and Unity stores
            // Matrix4x4 column-major too, so buffer[offset + col * 4 + row] is
            // the field m{row}{col}. Huawei's own Unity SDK relies on the same
            // layout when it marshals a projection matrix by reference.
            var matrix = new Matrix4x4();
            matrix.m00 = buffer[offset];
            matrix.m10 = buffer[offset + 1];
            matrix.m20 = buffer[offset + 2];
            matrix.m30 = buffer[offset + 3];
            matrix.m01 = buffer[offset + 4];
            matrix.m11 = buffer[offset + 5];
            matrix.m21 = buffer[offset + 6];
            matrix.m31 = buffer[offset + 7];
            matrix.m02 = buffer[offset + 8];
            matrix.m12 = buffer[offset + 9];
            matrix.m22 = buffer[offset + 10];
            matrix.m32 = buffer[offset + 11];
            matrix.m03 = buffer[offset + 12];
            matrix.m13 = buffer[offset + 13];
            matrix.m23 = buffer[offset + 14];
            matrix.m33 = buffer[offset + 15];
            return matrix;
        }
    }

    /// <summary>
    ///     one plane detected this frame
    /// </summary>
    public struct AREnginePlane
    {
        /// <summary>centre of the plane, in world metres</summary>
        public Pose centerPose;

        /// <summary>half-width along the plane's local X, in metres</summary>
        public float extentX;

        /// <summary>half-height along the plane's local Z, in metres</summary>
        public float extentZ;

        /// <summary>which way the plane faces</summary>
        public AREngine_ARPlaneType planeType;

        /// <summary>semantic label, only meaningful when semantic mode is on</summary>
        public AREngine_ARSemanticPlaneLabel label;

        /// <summary>tracking state of the plane itself</summary>
        public AREngine_ARTrackingState trackingState;

        /// <summary>
        ///     id to pass to AREngineSession.CreateAnchorOnPlane or
        ///     AREngineSession.GetPlanePolygon
        /// </summary>
        public int trackableId;
    }

    /// <summary>
    ///     one hit-test result
    /// </summary>
    public struct AREngineHit
    {
        /// <summary>pose of the hit, oriented to the surface normal</summary>
        public Pose pose;

        /// <summary>distance from the camera to the hit, in metres</summary>
        public float distance;

        /// <summary>what kind of thing was hit</summary>
        public AREngine_ARTrackableType trackableType;

        /// <summary>tracking state of the thing that was hit</summary>
        public AREngine_ARTrackingState trackingState;

        /// <summary>id to pass to AREngineSession.CreateAnchorOnTrackable</summary>
        public int trackableId;
    }

    /// <summary>
    ///     one anchor attached to the map
    /// </summary>
    public struct AREngineAnchor
    {
        /// <summary>
        ///     id to pass to AREngineSession.GetAnchorPose,
        ///     AREngineSession.DetachAnchor and AREngineSession.ReleaseAnchor
        /// </summary>
        public int anchorId;

        /// <summary>current pose of the anchor, in world metres</summary>
        public Pose pose;

        /// <summary>tracking state; a paused anchor's pose is stale</summary>
        public AREngine_ARTrackingState trackingState;
    }

    /// <summary>
    ///     a generic trackable, for the types that have no record layout of
    ///     their own
    /// </summary>
    public struct AREngineTrackable
    {
        /// <summary>id to pass to AREngineSession.CreateAnchorOnTrackable</summary>
        public int trackableId;

        /// <summary>what kind of trackable this is</summary>
        public AREngine_ARTrackableType trackableType;

        /// <summary>tracking state</summary>
        public AREngine_ARTrackingState trackingState;
    }

    /// <summary>
    ///     one augmented image tracked this frame
    /// </summary>
    public struct AREngineTrackedImage
    {
        /// <summary>centre of the image, in world metres</summary>
        public Pose centerPose;

        /// <summary>physical width of the image, in metres</summary>
        public float extentX;

        /// <summary>physical height of the image, in metres</summary>
        public float extentZ;

        /// <summary>index of this image in the database it was added to</summary>
        public int databaseIndex;

        /// <summary>tracking state</summary>
        public AREngine_ARTrackingState trackingState;

        /// <summary>
        ///     name given to AddImageToDatabase; empty until
        ///     AREngineSession.GetTrackedImages is called with readNames set
        /// </summary>
        public string imageName;

        /// <summary>
        ///     id to pass to AREngineSession.CreateAnchorOnTrackable,
        ///     AREngineSession.TryGetImageInfo and
        ///     AREngineSession.GetTrackedImageNameById; valid for the whole session
        /// </summary>
        public int trackableId;
    }

    /// <summary>
    ///     one face tracked this frame
    /// </summary>
    public struct AREngineFace
    {
        /// <summary>centre of the face, in world metres</summary>
        public Pose centerPose;

        /// <summary>tracking state</summary>
        public AREngine_ARTrackingState trackingState;

        /// <summary>
        ///     id to pass to AREngineSession.CreateAnchorOnTrackable,
        ///     AREngineSession.AcquireFaceGeometryById and
        ///     AREngineSession.CopyFaceBlendShapesById; valid for the whole session
        /// </summary>
        public int trackableId;
    }

    /// <summary>
    ///     one body tracked this frame
    /// </summary>
    public struct AREngineBody
    {
        /// <summary>stable id AREngine assigns to this person</summary>
        public int trackId;

        /// <summary>
        ///     how many skeleton points to expect from
        ///     AREngineSession.CopyBodySkeleton2D
        /// </summary>
        public int pointCount;

        /// <summary>
        ///     how many bones to expect; AREngineSession.CopyBodyConnections
        ///     fills twice this many ints
        /// </summary>
        public int connectionCount;

        /// <summary>tracking state</summary>
        public AREngine_ARTrackingState trackingState;

        /// <summary>capture time in nanoseconds</summary>
        public long timestampNs;
    }

    /// <summary>
    ///     one recognised object shape this frame
    /// </summary>
    public struct AREngineTarget
    {
        /// <summary>centre of the shape, in world metres</summary>
        public Pose centerPose;

        /// <summary>which primitive AREngine matched</summary>
        public AREngine_ARTargetShapeLabel shape;

        /// <summary>radius in metres, for circle and cube shapes</summary>
        public float radius;

        /// <summary>minimum bounding box, in world metres</summary>
        public Bounds bounds;
    }

    /// <summary>
    ///     the camera's intrinsic parameters for the current preview size
    /// </summary>
    public struct AREngineCameraIntrinsics
    {
        /// <summary>focal length in pixels along X</summary>
        public float focalLengthX;

        /// <summary>focal length in pixels along Y</summary>
        public float focalLengthY;

        /// <summary>principal point in pixels along X</summary>
        public float principalPointX;

        /// <summary>principal point in pixels along Y</summary>
        public float principalPointY;

        /// <summary>width of the image these intrinsics describe, in pixels</summary>
        public int imageWidth;

        /// <summary>height of the image these intrinsics describe, in pixels</summary>
        public int imageHeight;

        /// <summary>
        ///     the five AREngine distortion coefficients, or null when the device
        ///     reported none
        /// </summary>
        public float[] distortion;
    }

    /// <summary>
    ///     everything the camera and pose data for one frame
    /// </summary>
    public struct AREngineFrame
    {
        /// <summary>false when the frame could not be read and the rest is stale</summary>
        public bool isValid;

        /// <summary>
        ///     display-oriented camera pose: the one to assign to the Unity
        ///     camera's transform
        /// </summary>
        public Pose cameraPose;

        /// <summary>raw camera pose, before display-orientation correction</summary>
        public Pose rawCameraPose;

        /// <summary>view matrix matching rawCameraPose</summary>
        public Matrix4x4 viewMatrix;

        /// <summary>
        ///     projection matrix for the current clip planes, ready to assign to
        ///     Camera.projectionMatrix
        /// </summary>
        public Matrix4x4 projectionMatrix;

        /// <summary>tracking state of the camera</summary>
        public AREngine_ARTrackingState trackingState;

        /// <summary>why tracking is degraded, when it is</summary>
        public AREngine_ARTrackingStateReason trackingStateReason;

        /// <summary>capture time in nanoseconds</summary>
        public long timestampNs;
    }

    /// <summary>
    ///     the settings AREngineSession.Create applies before resuming
    /// </summary>
    /// <remarks>
    ///     defaults to world tracking with horizontal and vertical plane finding,
    ///     which is what most placement-style apps want. Features that need a
    ///     capability check first (depth, mesh, face, body, image) are left off.
    /// </remarks>
    [Serializable]
    public sealed class AREngineSessionConfig
    {
        /// <summary>
        ///     which tracking subsystems to run, OR-ed together; despite the name
        ///     AREngine_ARType is a bitmask, not a set of alternatives
        /// </summary>
        public AREngine_ARType arType = AREngine_ARType.ARENGINE_TYPE_WORLD;

        /// <summary>which planes to look for</summary>
        public AREngine_ARPlaneFindingMode planeFindingMode =
            AREngine_ARPlaneFindingMode.ARENGINE_PLANE_FINDING_MODE_HORIZONTAL_AND_VERTICAL;

        /// <summary>camera focus behaviour</summary>
        public AREngine_ARFocusMode focusMode = AREngine_ARFocusMode.ARENGINE_FOCUS_MODE_AUTO;

        /// <summary>
        ///     whether Update blocks until a new frame is ready; blocking is the
        ///     safer choice because it keeps the pose and the camera texture in
        ///     step
        /// </summary>
        public AREngine_ARUpdateMode updateMode = AREngine_ARUpdateMode.ARENGINE_UPDATE_MODE_BLOCKING;

        /// <summary>power / performance trade-off</summary>
        public AREngine_ARPowerMode powerMode = AREngine_ARPowerMode.ARENGINE_POWER_MODE_NORMAL;

        /// <summary>depth estimation</summary>
        public AREngine_ARDepthMode depthMode = AREngine_ARDepthMode.ARENGINE_DEPTH_MODE_DISABLED;

        /// <summary>scene mesh reconstruction</summary>
        public AREngine_ARMeshMode meshMode = AREngine_ARMeshMode.ARENGINE_MESH_MODE_DISABLED;

        /// <summary>plane and object semantics</summary>
        public AREngine_ARSemanticMode semanticMode = AREngine_ARSemanticMode.ARENGINE_SEMANTIC_MODE_NONE;

        /// <summary>high-precision dense geometry</summary>
        public AREngine_ARSemanticDenseMode semanticDenseMode =
            AREngine_ARSemanticDenseMode.ARENGINE_SEMANTIC_DENSE_MODE_DISABLED;

        /// <summary>whether the CPU camera image is also produced</summary>
        public AREngine_ARImageStreamMode imageStreamMode =
            AREngine_ARImageStreamMode.ARENGINE_IMAGE_STREAM_MODE_PREVIEW;

        /// <summary>which pose the camera reports</summary>
        public AREngine_ARPoseMode poseMode = AREngine_ARPoseMode.ARENGINE_POSE_MODE_GRAVITY;

        /// <summary>how many faces to track at once</summary>
        public AREngine_ARMultiFaceMode multiFaceMode = AREngine_ARMultiFaceMode.ARENGINE_MULTIFACE_DISABLE;

        /// <summary>front or back camera</summary>
        public AREngine_ARCameraLensFacing cameraLensFacing =
            AREngine_ARCameraLensFacing.ARENGINE_CAMERA_FACING_REAR;

        /// <summary>preview stream behaviour</summary>
        public AREngine_ARPreviewMode previewMode = AREngine_ARPreviewMode.ARENGINE_PREVIEW_MODE_ENABLED;

        /// <summary>requested preview width in pixels, or 0 to let AREngine choose</summary>
        public int previewWidth;

        /// <summary>requested preview height in pixels, or 0 to let AREngine choose</summary>
        public int previewHeight;

        /// <summary>
        ///     map memory budget in megabytes, or 0 to keep the AREngine default;
        ///     the SDK accepts 100 MB to 16 GB
        /// </summary>
        public long maxMapSize;

        /// <summary>how many bodies to track at once, or 0 to keep the AREngine default</summary>
        public int bodyDetectedNum;
    }
}
