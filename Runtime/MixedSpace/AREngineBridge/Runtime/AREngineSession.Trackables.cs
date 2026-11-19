// Trackable, anchor and sensor getters for libAREngineBridge.so.
//
// Second half of AREngineSession. Everything here reads the fixed-stride float
// records described in tjarex.h into caller-supplied lists, reusing one scratch
// buffer so a frame allocates nothing.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AREngineBridge
{
    public static partial class AREngineSession
    {
        private const int PointFloats = 4;
        private const int SemanticPointFloats = 9;
        private const int SemanticCubeFloats = 28;
        private const int ImageNameChars = 128;

        #region Planes

        /// <summary>
        ///     reads every plane currently tracked
        /// </summary>
        /// <param name="results">cleared, then filled with the planes</param>
        /// <param name="maxRecords">how many planes to read at most</param>
        /// <returns>how many planes were read</returns>
        public static int GetPlanes(List<AREnginePlane> results, int maxRecords = DefaultMaxRecords)
        {
            return ReadPlanes(results, AREngineExtended.GetPlanes(RecordBuffer(maxRecords), maxRecords));
        }

        /// <summary>
        ///     reads only the planes the last Update changed, which is cheaper than
        ///     GetPlanes when the caller keeps its own list
        /// </summary>
        /// <param name="results">cleared, then filled with the changed planes</param>
        /// <param name="maxRecords">how many planes to read at most</param>
        /// <returns>how many planes were read</returns>
        public static int GetUpdatedPlanes(List<AREnginePlane> results, int maxRecords = DefaultMaxRecords)
        {
            return ReadPlanes(results, AREngineExtended.GetUpdatedPlanes(RecordBuffer(maxRecords), maxRecords));
        }

        /// <summary>
        ///     reads a plane's boundary polygon in the plane's local space
        /// </summary>
        /// <param name="trackableId">the plane's trackableId</param>
        /// <param name="results">cleared, then filled with local XZ pairs</param>
        /// <param name="maxVertices">how many vertices to read at most</param>
        /// <returns>how many vertices were read</returns>
        /// <remarks>
        ///     the points are local to the plane's centre pose, so transform them
        ///     by AREnginePlane.centerPose to reach world space
        /// </remarks>
        public static int GetPlanePolygon(int trackableId, List<Vector2> results, int maxVertices = DefaultMaxRecords)
        {
            results.Clear();
            var written = AREngineExtended.GetPlanePolygon(trackableId, FloatBuffer(maxVertices * 2), maxVertices * 2);
            if (written < 0)
            {
                return Failed("plane polygon", written);
            }

            for (var i = 0; i + 1 < written; i += 2)
            {
                results.Add(new Vector2(_floats[i], _floats[i + 1]));
            }

            return results.Count;
        }

        /// <summary>
        ///     tests whether a pose lies inside a plane's extents, which is the bounding
        ///     rectangle rather than the detected surface
        /// </summary>
        /// <param name="trackableId">the plane's trackableId</param>
        /// <param name="pose">the pose to test, in world metres</param>
        /// <returns>true when the pose is inside the plane's extents</returns>
        public static bool IsPoseInPlaneExtents(int trackableId, Pose pose)
        {
            AREngineRecord.WritePose(pose, _poseRaw, 0);
            return AREngineExtended.IsPoseInPlaneExtents(trackableId, _poseRaw) != 0;
        }

        /// <summary>
        ///     tests whether a pose lies inside a plane's detected boundary polygon. Stricter
        ///     than <see cref="IsPoseInPlaneExtents"/>, so prefer this before placing content
        ///     that must sit on the real surface
        /// </summary>
        /// <param name="trackableId">the plane's trackableId</param>
        /// <param name="pose">the pose to test, in world metres</param>
        /// <returns>true when the pose is inside the plane's polygon</returns>
        public static bool IsPoseInPlanePolygon(int trackableId, Pose pose)
        {
            AREngineRecord.WritePose(pose, _poseRaw, 0);
            return AREngineExtended.IsPoseInPlanePolygon(trackableId, _poseRaw) != 0;
        }

        /// <summary>
        ///     reads the plane that has absorbed this one, if any
        /// </summary>
        /// <param name="trackableId">the plane's trackableId</param>
        /// <returns>the subsumer's trackableId, or -1 when nothing has absorbed it</returns>
        /// <remarks>
        ///     AREngine merges coplanar planes as the scene is refined, and a plane
        ///     that has been subsumed covers no real surface any more, so a visualizer
        ///     should stop drawing it once this returns anything but -1
        /// </remarks>
        public static int GetPlaneSubsumedBy(int trackableId)
        {
            return AREngineExtended.GetPlaneSubsumedBy(trackableId);
        }

        /// <summary>
        ///     reads one plane's record by id, without scanning a batch
        /// </summary>
        /// <param name="trackableId">the plane's trackableId</param>
        /// <param name="plane">the plane's current record, when the call succeeds</param>
        /// <returns>
        ///     true when the record was read. False means the id is unknown or does
        ///     not name a plane, which is the case for a trackable that has stopped
        ///     being tracked long enough to leave the map
        /// </returns>
        /// <remarks>
        ///     <see cref="GetPlaneSubsumedBy"/> can hand back the id of a plane this
        ///     caller has never seen in a batch, so this is how that record is read
        /// </remarks>
        public static bool TryGetPlaneInfo(int trackableId, out AREnginePlane plane)
        {
            plane = default;
            if (AREngineExtended.GetPlaneInfo(trackableId, RecordBuffer(1)) !=
                AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            plane = new AREnginePlane
            {
                centerPose = AREngineRecord.ReadPose(_records, AREngineX.Plane.Quat),
                extentX = _records[AREngineX.Plane.ExtentX],
                extentZ = _records[AREngineX.Plane.ExtentZ],
                planeType = (AREngine_ARPlaneType)(int)_records[AREngineX.Plane.Type],
                label = (AREngine_ARSemanticPlaneLabel)(int)_records[AREngineX.Plane.Label],
                trackingState = (AREngine_ARTrackingState)(int)_records[AREngineX.Plane.State],
                trackableId = (int)_records[AREngineX.Plane.Trackable],
            };
            return true;
        }

        /// <summary>
        ///     reads a plane's boundary polygon in world metres, ready to build a mesh
        ///     from. This is <see cref="GetPlanePolygon(int,List{Vector2},int)"/> with the
        ///     plane's centre pose already applied
        /// </summary>
        /// <param name="plane">the plane record, whose trackableId and centerPose are used</param>
        /// <param name="results">cleared, then filled with world-space vertices</param>
        /// <param name="maxVertices">how many vertices to read at most</param>
        /// <returns>how many vertices were read</returns>
        public static int GetPlanePolygon(AREnginePlane plane, List<Vector3> results,
            int maxVertices = DefaultMaxRecords)
        {
            results.Clear();
            var buffer = FloatBuffer(maxVertices * 2);
            var written = AREngineExtended.GetPlanePolygon(plane.trackableId, buffer, maxVertices * 2);
            if (written < 0)
            {
                return Failed("plane polygon", written);
            }

            var center = plane.centerPose;
            for (var i = 0; i + 1 < written; i += 2)
            {
                var local = new Vector3(buffer[i], 0f, buffer[i + 1]);
                results.Add(center.position + center.rotation * local);
            }

            return results.Count;
        }

        /// <summary>
        ///     reads trackables of one type that have no record layout of their own
        /// </summary>
        /// <param name="trackableType">which kind of trackable to read</param>
        /// <param name="results">cleared, then filled with the trackables</param>
        /// <param name="maxRecords">how many to read at most</param>
        /// <returns>how many trackables were read</returns>
        /// <remarks>
        ///     a generic trackable carries identity and state only. Pass its
        ///     trackableId to <see cref="TryGetTrackablePose"/> for the pose, or use
        ///     the dedicated getter for planes, images, faces and bodies
        /// </remarks>
        public static int GetTrackables(AREngine_ARTrackableType trackableType, List<AREngineTrackable> results,
            int maxRecords = DefaultMaxRecords)
        {
            return ReadTrackableRecords(false, trackableType, results, maxRecords);
        }

        /// <summary>
        ///     reads only the trackables the last <see cref="Update"/> changed
        /// </summary>
        /// <param name="trackableType">which kind of trackable to read</param>
        /// <param name="results">cleared, then filled with the changed trackables</param>
        /// <param name="maxRecords">how many to read at most</param>
        /// <returns>how many trackables were read</returns>
        public static int GetUpdatedTrackables(AREngine_ARTrackableType trackableType,
            List<AREngineTrackable> results, int maxRecords = DefaultMaxRecords)
        {
            return ReadTrackableRecords(true, trackableType, results, maxRecords);
        }

        private static int ReadTrackableRecords(bool updatedOnly, AREngine_ARTrackableType trackableType,
            List<AREngineTrackable> results, int maxRecords)
        {
            results.Clear();
            var count = updatedOnly
                ? AREngineExtended.GetUpdatedTrackables((int)trackableType, RecordBuffer(maxRecords), maxRecords)
                : AREngineExtended.GetTrackables((int)trackableType, RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("trackables", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                results.Add(new AREngineTrackable
                {
                    trackableId = (int)_records[o + AREngineX.Trackable.Id],
                    trackableType = (AREngine_ARTrackableType)(int)_records[o + AREngineX.Trackable.Type],
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Trackable.State],
                });
            }

            return count;
        }

        /// <summary>
        ///     reads the pose of any trackable that has one
        /// </summary>
        /// <param name="trackableId">the id from a batched record</param>
        /// <param name="pose">the trackable's pose, when the call succeeds</param>
        /// <returns>
        ///     true when the pose was read. False means the id is unknown or the
        ///     trackable has no single pose, which is the case for a body
        /// </returns>
        public static bool TryGetTrackablePose(int trackableId, out Pose pose)
        {
            pose = Pose.identity;
            if (AREngineExtended.GetTrackablePose(trackableId, _poseRaw) != PoseRawFloats)
            {
                return false;
            }

            pose = AREngineRecord.ReadPose(_poseRaw, 0);
            return true;
        }

        /// <summary>
        ///     reads how a feature point suggests content be oriented
        /// </summary>
        /// <param name="trackableId">the id from a batched record</param>
        /// <param name="mode">the point's orientation mode, when the call succeeds</param>
        /// <returns>
        ///     false when the id is unknown or names something other than a point,
        ///     which is also the case for a point the SDK reports no pose for
        /// </returns>
        public static bool TryGetPointOrientationMode(int trackableId,
            out AREngine_ARPointOrientationMode mode)
        {
            mode = AREngine_ARPointOrientationMode.ARENGINE_POINT_ORIENTATION_INITIALIZED_TO_IDENTITY;
            var buffer = IntBuffer(1);
            if (AREngineExtended.GetPointOrientationMode(trackableId, buffer) !=
                AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            mode = (AREngine_ARPointOrientationMode)_ints[0];
            return true;
        }

        /// <summary>
        ///     reads one trackable's type and state by id, without scanning a batch
        /// </summary>
        /// <param name="trackableId">the id from a batched record</param>
        /// <param name="trackable">the trackable's current record, when read</param>
        /// <returns>false when the id is unknown to the bridge</returns>
        public static bool TryGetTrackableInfo(int trackableId, out AREngineTrackable trackable)
        {
            trackable = default;
            if (AREngineExtended.GetTrackableInfo(trackableId, RecordBuffer(1)) !=
                AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            trackable = new AREngineTrackable
            {
                trackableId = (int)_records[AREngineX.Trackable.Id],
                trackableType = (AREngine_ARTrackableType)(int)_records[AREngineX.Trackable.Type],
                trackingState = (AREngine_ARTrackingState)(int)_records[AREngineX.Trackable.State],
            };
            return true;
        }

        /// <summary>
        ///     reads the anchors attached to a trackable
        /// </summary>
        /// <param name="trackableId">the plane, image or hit trackableId</param>
        /// <param name="results">cleared, then filled with the anchor ids</param>
        /// <param name="maxRecords">how many anchors to read at most</param>
        /// <returns>how many anchor ids were read</returns>
        /// <remarks>
        ///     the ids come from the same table as <see cref="GetAnchors"/> uses, so
        ///     they work with TryGetAnchorPose and DetachAnchor straight away
        /// </remarks>
        public static int GetTrackableAnchorIds(int trackableId, List<int> results,
            int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.GetTrackableAnchorIds(trackableId, IntBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("trackable anchors", count);
            }

            for (var i = 0; i < count; i++)
            {
                results.Add(_ints[i]);
            }

            return count;
        }

        /// <summary>
        ///     reads the feature point cloud
        /// </summary>
        /// <param name="results">cleared, then filled with x, y, z and confidence per point</param>
        /// <param name="maxPoints">how many points to read at most</param>
        /// <returns>how many points were read</returns>
        public static int GetPointCloud(List<Vector4> results, int maxPoints = 1024)
        {
            results.Clear();
            var written = AREngineExtended.GetPointCloud(FloatBuffer(maxPoints * PointFloats), maxPoints * PointFloats);
            if (written < 0)
            {
                return Failed("point cloud", written);
            }

            for (var i = 0; i + PointFloats <= written; i += PointFloats)
            {
                results.Add(new Vector4(_floats[i], _floats[i + 1], _floats[i + 2], _floats[i + 3]));
            }

            return results.Count;
        }

        /// <summary>
        ///     how many points the current point cloud holds
        /// </summary>
        public static int PointCloudCount => AREngineExtended.GetPointCloudCount();

        /// <summary>
        ///     capture time of the current point cloud, in nanoseconds
        /// </summary>
        /// <remarks>0 when there is no cloud to read</remarks>
        public static long PointCloudTimestamp => AREngineExtended.GetPointCloudTimestamp();

        #endregion

        #region Hit testing

        /// <summary>
        ///     casts a ray from the camera through a pixel of the preview area
        /// </summary>
        /// <param name="pixelX">x in preview-area pixels</param>
        /// <param name="pixelY">y in preview-area pixels</param>
        /// <param name="results">cleared, then filled with the hits, nearest first</param>
        /// <param name="maxRecords">how many hits to read at most</param>
        /// <returns>how many hits were read</returns>
        /// <remarks>
        ///     the coordinates are preview-area pixels, not Unity screen pixels;
        ///     scale a touch position by the preview-to-screen ratio first
        /// </remarks>
        public static int HitTest(float pixelX, float pixelY, List<AREngineHit> results,
            int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.HitTest(pixelX, pixelY, RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("hit test", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                results.Add(new AREngineHit
                {
                    pose = AREngineRecord.ReadPose(_records, o + AREngineX.Hit.Quat),
                    distance = _records[o + AREngineX.Hit.Distance],
                    trackableType = (AREngine_ARTrackableType)(int)_records[o + AREngineX.Hit.Type],
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Hit.State],
                    trackableId = (int)_records[o + AREngineX.Hit.Trackable],
                });
            }

            return count;
        }

        #endregion

        #region Anchors

        /// <summary>
        ///     creates a free-floating anchor at a pose
        /// </summary>
        /// <param name="pose">where to pin the anchor, in world metres</param>
        /// <returns>the anchor id, or a negative value on failure</returns>
        public static int CreateAnchor(Pose pose)
        {
            AREngineRecord.WritePose(pose, _poseRaw, 0);
            return ReportId("create anchor", AREngineExtended.CreateAnchor(_poseRaw));
        }

        /// <summary>
        ///     creates an anchor attached to a trackable, which keeps it pinned as
        ///     the map refines
        /// </summary>
        /// <param name="trackableId">the plane, image or hit trackableId, which must still be tracked</param>
        /// <param name="pose">where to pin the anchor, in world metres</param>
        /// <returns>the anchor id, or a negative value on failure</returns>
        public static int CreateAnchorOnTrackable(int trackableId, Pose pose)
        {
            AREngineRecord.WritePose(pose, _poseRaw, 0);
            return ReportId("create anchor on trackable",
                AREngineExtended.CreateAnchorOnTrackable(trackableId, _poseRaw));
        }

        /// <summary>
        ///     reads every anchor the bridge holds
        /// </summary>
        /// <param name="results">cleared, then filled with the anchors</param>
        /// <param name="maxRecords">how many anchors to read at most</param>
        /// <returns>how many anchors were read</returns>
        public static int GetAnchors(List<AREngineAnchor> results, int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.GetAnchors(RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("anchors", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                results.Add(new AREngineAnchor
                {
                    anchorId = (int)_records[o + AREngineX.Anchor.Id],
                    pose = AREngineRecord.ReadPose(_records, o + AREngineX.Anchor.Quat),
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Anchor.State],
                });
            }

            return count;
        }

        /// <summary>
        ///     how many anchors the bridge holds
        /// </summary>
        public static int AnchorCount => AREngineExtended.GetAnchorCount();

        /// <summary>
        ///     reads one anchor's current pose
        /// </summary>
        /// <param name="anchorId">the id from GetAnchors or CreateAnchor</param>
        /// <param name="pose">the anchor's pose, when the call succeeds</param>
        /// <returns>true when the pose was read</returns>
        public static bool TryGetAnchorPose(int anchorId, out Pose pose)
        {
            pose = Pose.identity;
            if (AREngineExtended.GetAnchorPose(anchorId, _poseRaw) != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            pose = AREngineRecord.ReadPose(_poseRaw, 0);
            return true;
        }

        /// <summary>
        ///     reads one anchor's tracking state
        /// </summary>
        /// <param name="anchorId">the id from GetAnchors or CreateAnchor</param>
        /// <returns>the state; a paused anchor's pose is stale</returns>
        public static AREngine_ARTrackingState GetAnchorTrackingState(int anchorId)
        {
            return (AREngine_ARTrackingState)AREngineExtended.GetAnchorTrackingState(anchorId);
        }

        /// <summary>
        ///     stops tracking an anchor but keeps the object
        /// </summary>
        /// <param name="anchorId">the id to detach</param>
        /// <returns>ARENGINE_SUCCESS when detached</returns>
        public static AREngine_ARStatus DetachAnchor(int anchorId)
        {
            return ReportStatus("detach anchor", AREngineExtended.DetachAnchor(anchorId));
        }

        /// <summary>
        ///     releases the bridge's reference to one anchor, invalidating its id
        /// </summary>
        /// <param name="anchorId">the id to release</param>
        public static void ReleaseAnchor(int anchorId)
        {
            AREngineExtended.ReleaseAnchor(anchorId);
        }

        /// <summary>
        ///     releases every anchor the bridge holds
        /// </summary>
        public static void ReleaseAllAnchors()
        {
            AREngineExtended.ReleaseAllAnchors();
        }

        #endregion

        #region Scene mesh

        /// <summary>
        ///     acquires this frame's scene mesh; requires mesh mode enabled
        /// </summary>
        /// <returns>true when a mesh is available to copy</returns>
        public static bool AcquireSceneMesh()
        {
            var status = AREngineExtended.AcquireSceneMesh();
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogWarning($"arengine: scene mesh not available: {GetStatusText(status)}");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     how many vertices the acquired scene mesh holds
        /// </summary>
        public static int SceneMeshVertexCount => AREngineExtended.GetSceneMeshVertexCount();

        /// <summary>
        ///     how many triangles' worth of indices the acquired scene mesh holds
        /// </summary>
        public static int SceneMeshIndexCount => AREngineExtended.GetSceneMeshIndexCount();

        /// <summary>
        ///     copies the acquired scene mesh vertices
        /// </summary>
        /// <param name="results">cleared, then filled with vertex positions in metres</param>
        /// <returns>how many vertices were read</returns>
        public static int CopySceneMeshVertices(List<Vector3> results)
        {
            return CopyTriplets(results, SceneMeshVertexCount,
                (buffer, max) => AREngineExtended.CopySceneMeshVertices(buffer, max), "scene mesh vertices");
        }

        /// <summary>
        ///     copies the acquired scene mesh normals
        /// </summary>
        /// <param name="results">cleared, then filled with unit normals</param>
        /// <returns>how many normals were read</returns>
        public static int CopySceneMeshNormals(List<Vector3> results)
        {
            return CopyTriplets(results, SceneMeshVertexCount,
                (buffer, max) => AREngineExtended.CopySceneMeshNormals(buffer, max), "scene mesh normals");
        }

        /// <summary>
        ///     copies the acquired scene mesh triangle indices
        /// </summary>
        /// <param name="results">cleared, then filled with indices into the vertex list</param>
        /// <returns>how many indices were read</returns>
        public static int CopySceneMeshIndices(List<int> results)
        {
            results.Clear();
            var count = SceneMeshIndexCount;
            if (count <= 0)
            {
                return 0;
            }

            var written = AREngineExtended.CopySceneMeshIndices(IntBuffer(count), count);
            if (written < 0)
            {
                return Failed("scene mesh indices", written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add(_ints[i]);
            }

            return written;
        }

        /// <summary>
        ///     releases the acquired scene mesh
        /// </summary>
        public static void ReleaseSceneMesh()
        {
            AREngineExtended.ReleaseSceneMesh();
        }

        #endregion

        #region Depth

        /// <summary>
        ///     copies the 16-bit depth image; requires depth mode enabled
        /// </summary>
        /// <param name="buffer">destination, at least width * height entries</param>
        /// <param name="width">image width in pixels, when the call succeeds</param>
        /// <param name="height">image height in pixels, when the call succeeds</param>
        /// <returns>how many depth samples were written, 0 on failure</returns>
        public static int CopyDepthImage16(ushort[] buffer, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (buffer == null || buffer.Length == 0)
            {
                return 0;
            }

            var written = AREngineExtended.CopyDepthImage16(buffer, buffer.Length, out width, out height);
            if (written < 0)
            {
                Failed("depth image", written);
                width = 0;
                height = 0;
                return 0;
            }

            if (written == 0)
            {
                width = 0;
                height = 0;
            }

            return written;
        }

        /// <summary>
        ///     copies the per-pixel depth confidence; requires depth mode enabled
        /// </summary>
        /// <param name="buffer">destination, at least width * height entries</param>
        /// <param name="width">image width in pixels, when the call succeeds</param>
        /// <param name="height">image height in pixels, when the call succeeds</param>
        /// <returns>how many confidence values were written, 0 on failure</returns>
        public static int CopyDepthConfidence(byte[] buffer, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (buffer == null || buffer.Length == 0)
            {
                return 0;
            }

            var written = AREngineExtended.CopyDepthConfidence(buffer, buffer.Length, out width, out height);
            if (written < 0)
            {
                Failed("depth confidence", written);
                width = 0;
                height = 0;
                return 0;
            }

            if (written == 0)
            {
                width = 0;
                height = 0;
            }

            return written;
        }

        #endregion

        #region CPU camera image

        /// <summary>
        ///     acquires this frame's camera image for CPU access
        /// </summary>
        /// <returns>true when an image is available to copy</returns>
        /// <remarks>
        ///     this is the only way to read pixels: the OES preview texture is not
        ///     readable. The image must be released before the next Update
        /// </remarks>
        public static bool AcquireCameraImage()
        {
            var status = AREngineExtended.AcquireCameraImage();
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogWarning($"arengine: camera image not available: {GetStatusText(status)}");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     reads the acquired camera image's geometry
        /// </summary>
        /// <param name="width">image width in pixels</param>
        /// <param name="height">image height in pixels</param>
        /// <param name="format">AREngine_ARImageFormat value</param>
        /// <param name="planeCount">how many planes the format has</param>
        /// <returns>true when the image info was read</returns>
        public static bool TryGetCameraImageInfo(out int width, out int height, out int format, out int planeCount)
        {
            var status = AREngineExtended.GetCameraImageInfo(out width, out height, out format, out planeCount);
            if (status < 0)
            {
                Failed("camera image info", status);
                width = 0;
                height = 0;
                format = 0;
                planeCount = 0;
                return false;
            }

            return width > 0 && height > 0;
        }

        /// <summary>
        ///     copies one plane of the acquired camera image
        /// </summary>
        /// <param name="planeIndex">which plane, 0 being luma for YUV formats</param>
        /// <param name="buffer">destination, at least rowStride * height bytes</param>
        /// <param name="rowStride">bytes per row, which may exceed the width</param>
        /// <param name="pixelStride">bytes per pixel within a row</param>
        /// <param name="dataLength">the plane's full byte length, which can exceed the return value when the buffer was too small</param>
        /// <returns>how many bytes were copied, or a negative value on failure</returns>
        /// <remarks>
        ///     rows are padded to rowStride, so a consumer building a Texture2D has
        ///     to copy row by row rather than assuming width * height contiguous
        ///     bytes
        /// </remarks>
        public static int CopyCameraImagePlane(int planeIndex, byte[] buffer, out int rowStride, out int pixelStride,
            out int dataLength)
        {
            rowStride = 0;
            pixelStride = 0;
            dataLength = 0;
            if (buffer == null || buffer.Length == 0)
            {
                return -1;
            }

            return AREngineExtended.CopyCameraImagePlane(planeIndex, buffer, buffer.Length, out rowStride,
                out pixelStride, out dataLength);
        }

        /// <summary>
        ///     borrows the SDK's own pointer to one plane of the acquired camera image
        ///     rather than copying it
        /// </summary>
        /// <param name="planeIndex">which plane, 0 being luma for YUV formats</param>
        /// <param name="rowStride">bytes per row, which may exceed the width</param>
        /// <param name="pixelStride">bytes per pixel within a row</param>
        /// <param name="dataLength">the plane's full byte length</param>
        /// <returns>the plane's first byte, or IntPtr.Zero on failure</returns>
        /// <remarks>
        ///     prefer this over <see cref="CopyCameraImagePlane"/> when the caller has to
        ///     reorder rows or skip row-stride padding, because it can do both in a
        ///     single pass instead of copying the whole plane and then rewriting it.
        ///     the pointer is borrowed from the SDK: it is valid only until
        ///     <see cref="ReleaseCameraImage"/>, so never hold it across frames
        /// </remarks>
        public static IntPtr GetCameraImagePlaneData(int planeIndex, out int rowStride, out int pixelStride,
            out int dataLength)
        {
            rowStride = 0;
            pixelStride = 0;
            dataLength = 0;

            var status = AREngineExtended.GetCameraImagePlaneData(planeIndex, out IntPtr data, out rowStride,
                out pixelStride, out dataLength);
            if (status < 0)
            {
                Failed("camera image plane", status);
                return IntPtr.Zero;
            }

            return data;
        }

        /// <summary>
        ///     releases the acquired camera image
        /// </summary>
        public static void ReleaseCameraImage()
        {
            AREngineExtended.ReleaseCameraImage();
        }

        #endregion

        #region Image tracking

        /// <summary>
        ///     creates an empty augmented-image database
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when the database is ready</returns>
        public static AREngine_ARStatus CreateImageDatabase()
        {
            return ReportStatus("create image database", AREngineExtended.CreateImageDatabase());
        }

        /// <summary>
        ///     adds a grayscale image to the database
        /// </summary>
        /// <param name="gray">one byte per pixel, row-major</param>
        /// <param name="width">image width in pixels</param>
        /// <param name="height">image height in pixels</param>
        /// <param name="stride">bytes per row, which may exceed the width</param>
        /// <param name="imageName">the name reported back by GetTrackedImages</param>
        /// <param name="realWidthInMetres">physical width, or 0 to keep the AREngine default</param>
        /// <param name="index">the database index, when the call succeeds</param>
        /// <param name="reason">why the image was rejected, when it was</param>
        /// <returns>ARENGINE_SUCCESS when the image was accepted</returns>
        public static AREngine_ARStatus AddImageToDatabase(byte[] gray, int width, int height, int stride,
            string imageName, float realWidthInMetres, out int index,
            out AREngine_ARAddAugmentedImageReason reason)
        {
            index = 0;
            var rawReason = 0;
            var status = AREngineExtended.AddImageToDatabase(gray, width, height, stride, imageName,
                realWidthInMetres, out index, out rawReason);
            reason = (AREngine_ARAddAugmentedImageReason)rawReason;
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogError($"arengine: error: failed to add image '{imageName}': {GetStatusText(status)} ({reason})");
            }

            return status;
        }

        /// <summary>
        ///     loads a database serialised earlier by AREngine
        /// </summary>
        /// <param name="data">the serialised bytes</param>
        /// <param name="length">how many bytes of data to read</param>
        /// <returns>ARENGINE_SUCCESS when the database was loaded</returns>
        public static AREngine_ARStatus DeserializeImageDatabase(byte[] data, int length)
        {
            return ReportStatus("deserialize image database", AREngineExtended.DeserializeImageDatabase(data, length));
        }

        /// <summary>
        ///     how many images the database holds
        /// </summary>
        public static int ImageDatabaseCount => AREngineExtended.GetImageDatabaseCount();

        /// <summary>
        ///     reads the augmented images tracked this frame
        /// </summary>
        /// <param name="results">cleared, then filled with the tracked images</param>
        /// <param name="readNames">also fetch each image's name, which costs one extra call per image</param>
        /// <param name="maxRecords">how many images to read at most</param>
        /// <returns>how many images were read</returns>
        public static int GetTrackedImages(List<AREngineTrackedImage> results, bool readNames = false,
            int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.GetTrackedImages(RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("tracked images", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                var image = new AREngineTrackedImage
                {
                    centerPose = AREngineRecord.ReadPose(_records, o + AREngineX.Image.Quat),
                    extentX = _records[o + AREngineX.Image.ExtentX],
                    extentZ = _records[o + AREngineX.Image.ExtentZ],
                    databaseIndex = (int)_records[o + AREngineX.Image.Index],
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Image.State],
                    imageName = readNames ? ReadImageName(i) : string.Empty,
                    trackableId = (int)_records[o + AREngineX.Image.Trackable],
                };
                results.Add(image);
            }

            return count;
        }

        /// <summary>
        ///     reads one tracked image's record by id, without scanning a batch
        /// </summary>
        /// <param name="trackableId">the id from a batched record</param>
        /// <param name="image">the image's current record, when the call succeeds</param>
        /// <returns>false when the id is unknown or does not name an image</returns>
        /// <remarks>
        ///     unlike <see cref="GetTrackedImages"/>, this always reads the image's
        ///     name, because a caller querying one id by hand wants the whole record
        /// </remarks>
        public static bool TryGetImageInfo(int trackableId, out AREngineTrackedImage image)
        {
            image = default;
            if (AREngineExtended.GetImageInfo(trackableId, RecordBuffer(1)) !=
                AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            image = new AREngineTrackedImage
            {
                centerPose = AREngineRecord.ReadPose(_records, AREngineX.Image.Quat),
                extentX = _records[AREngineX.Image.ExtentX],
                extentZ = _records[AREngineX.Image.ExtentZ],
                databaseIndex = (int)_records[AREngineX.Image.Index],
                trackingState = (AREngine_ARTrackingState)(int)_records[AREngineX.Image.State],
                trackableId = (int)_records[AREngineX.Image.Trackable],
            };
            image.imageName = GetTrackedImageNameById(trackableId);
            return true;
        }

        /// <summary>
        ///     reads one tracked image's name by id
        /// </summary>
        /// <param name="trackableId">the id from a batched record</param>
        /// <returns>the name, or an empty string when the id is unknown</returns>
        public static string GetTrackedImageNameById(int trackableId)
        {
            var written = AREngineExtended.GetTrackedImageNameById(trackableId, ByteBuffer(ImageNameChars),
                ImageNameChars);
            return written <= 0 ? string.Empty : Encoding.UTF8.GetString(_bytes, 0, written);
        }

        #endregion

        #region Face tracking

        /// <summary>
        ///     reads the faces tracked this frame; requires the face AR type
        /// </summary>
        /// <param name="results">cleared, then filled with the faces</param>
        /// <param name="maxRecords">how many faces to read at most</param>
        /// <returns>how many faces were read</returns>
        /// <remarks>
        ///     each record's trackableId is valid for the whole session, so a face can
        ///     be followed across frames; the list position is not, and is only what
        ///     the recordIndex-taking getters below accept
        /// </remarks>
        public static int GetFaces(List<AREngineFace> results, int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.GetFaces(RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("faces", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                results.Add(new AREngineFace
                {
                    centerPose = AREngineRecord.ReadPose(_records, o + AREngineX.Face.Quat),
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Face.State],
                    trackableId = (int)_records[o + AREngineX.Face.Trackable],
                });
            }

            return count;
        }

        /// <summary>
        ///     reads one face's record by id, without scanning a batch
        /// </summary>
        /// <param name="trackableId">the id from a batched record</param>
        /// <param name="face">the face's current record, when the call succeeds</param>
        /// <returns>false when the id is unknown or does not name a face</returns>
        public static bool TryGetFaceInfo(int trackableId, out AREngineFace face)
        {
            face = default;
            if (AREngineExtended.GetFaceInfo(trackableId, RecordBuffer(1)) !=
                AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                return false;
            }

            face = new AREngineFace
            {
                centerPose = AREngineRecord.ReadPose(_records, AREngineX.Face.Quat),
                trackingState = (AREngine_ARTrackingState)(int)_records[AREngineX.Face.State],
                trackableId = (int)_records[AREngineX.Face.Trackable],
            };
            return true;
        }

        /// <summary>
        ///     acquires one face's mesh for the copy helpers below
        /// </summary>
        /// <param name="recordIndex">the face's position in the last GetFaces list</param>
        /// <returns>true when geometry is available to copy</returns>
        public static bool AcquireFaceGeometry(int recordIndex)
        {
            var status = AREngineExtended.AcquireFaceGeometry(recordIndex);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogWarning($"arengine: face geometry {recordIndex} not available: {GetStatusText(status)}");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     acquires one face's mesh by id, for a caller that has followed the face
        ///     across frames rather than kept a position in the last batch
        /// </summary>
        /// <param name="trackableId">the face's trackableId</param>
        /// <returns>true when geometry is available to copy</returns>
        public static bool AcquireFaceGeometryById(int trackableId)
        {
            var status = AREngineExtended.AcquireFaceGeometryById(trackableId);
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogWarning($"arengine: face geometry {trackableId} not available: {GetStatusText(status)}");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     how many vertices the acquired face mesh holds
        /// </summary>
        public static int FaceVertexCount => AREngineExtended.GetFaceVertexCount();

        /// <summary>
        ///     how many indices the acquired face mesh holds
        /// </summary>
        public static int FaceIndexCount => AREngineExtended.GetFaceIndexCount();

        /// <summary>
        ///     copies the acquired face mesh vertices, in metres relative to the face pose
        /// </summary>
        /// <param name="results">cleared, then filled with vertex positions</param>
        /// <returns>how many vertices were read</returns>
        public static int CopyFaceVertices(List<Vector3> results)
        {
            return CopyTriplets(results, FaceVertexCount,
                (buffer, max) => AREngineExtended.CopyFaceVertices(buffer, max), "face vertices");
        }

        /// <summary>
        ///     copies the acquired face mesh texture coordinates
        /// </summary>
        /// <param name="results">cleared, then filled with UVs</param>
        /// <returns>how many UVs were read</returns>
        public static int CopyFaceTexCoords(List<Vector2> results)
        {
            results.Clear();
            var written = AREngineExtended.CopyFaceTexCoords(FloatBuffer(FaceVertexCount * 2), FaceVertexCount * 2);
            if (written < 0)
            {
                return Failed("face texture coordinates", written);
            }

            for (var i = 0; i + 1 < written; i += 2)
            {
                results.Add(new Vector2(_floats[i], _floats[i + 1]));
            }

            return results.Count;
        }

        /// <summary>
        ///     copies the acquired face mesh triangle indices
        /// </summary>
        /// <param name="results">cleared, then filled with indices into the vertex list</param>
        /// <returns>how many indices were read</returns>
        public static int CopyFaceIndices(List<int> results)
        {
            results.Clear();
            var count = FaceIndexCount;
            if (count <= 0)
            {
                return 0;
            }

            var written = AREngineExtended.CopyFaceIndices(IntBuffer(count), count);
            if (written < 0)
            {
                return Failed("face indices", written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add(_ints[i]);
            }

            return written;
        }

        /// <summary>
        ///     releases the acquired face mesh
        /// </summary>
        public static void ReleaseFaceGeometry()
        {
            AREngineExtended.ReleaseFaceGeometry();
        }

        /// <summary>
        ///     copies one face's animoji blendshape weights
        /// </summary>
        /// <param name="recordIndex">the face's position in the last GetFaces list</param>
        /// <param name="weights">cleared, then filled with one weight per shape</param>
        /// <param name="shapes">cleared, then filled with the matching shape per weight</param>
        /// <param name="maxCount">how many shapes to read at most</param>
        /// <returns>how many shapes were read</returns>
        public static int CopyFaceBlendShapes(int recordIndex, List<float> weights,
            List<AREngine_ARAnimojiBlendShape> shapes, int maxCount = 64)
        {
            return FillBlendShapes(AREngineExtended.CopyFaceBlendShapes(recordIndex, FloatBuffer(maxCount),
                IntBuffer(maxCount), maxCount), weights, shapes);
        }

        /// <summary>
        ///     copies one face's animoji blendshape weights, addressed by id
        /// </summary>
        /// <param name="trackableId">the face's trackableId</param>
        /// <param name="weights">cleared, then filled with one weight per shape</param>
        /// <param name="shapes">cleared, then filled with the matching shape per weight</param>
        /// <param name="maxCount">how many shapes to read at most</param>
        /// <returns>how many shapes were read</returns>
        public static int CopyFaceBlendShapesById(int trackableId, List<float> weights,
            List<AREngine_ARAnimojiBlendShape> shapes, int maxCount = 64)
        {
            return FillBlendShapes(AREngineExtended.CopyFaceBlendShapesById(trackableId, FloatBuffer(maxCount),
                IntBuffer(maxCount), maxCount), weights, shapes);
        }

        private static int FillBlendShapes(int written, List<float> weights,
            List<AREngine_ARAnimojiBlendShape> shapes)
        {
            weights.Clear();
            shapes.Clear();
            if (written < 0)
            {
                return Failed("face blendshapes", written);
            }

            for (var i = 0; i < written; i++)
            {
                weights.Add(_floats[i]);
                shapes.Add((AREngine_ARAnimojiBlendShape)_ints[i]);
            }

            return written;
        }

        #endregion

        #region Body tracking

        /// <summary>
        ///     reads the bodies tracked this frame; requires the body AR type
        /// </summary>
        /// <param name="results">cleared, then filled with the bodies</param>
        /// <param name="maxRecords">how many bodies to read at most</param>
        /// <returns>how many bodies were read</returns>
        /// <remarks>
        ///     a body's position in the list is the recordIndex the skeleton getters
        ///     take, and it is only valid until the next Update
        /// </remarks>
        public static int GetBodies(List<AREngineBody> results, int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.GetBodies(RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("bodies", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                results.Add(new AREngineBody
                {
                    trackId = (int)_records[o + AREngineX.Body.TrackId],
                    pointCount = (int)_records[o + AREngineX.Body.PointCount],
                    connectionCount = (int)_records[o + AREngineX.Body.ConnectionCount],
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Body.State],
                    timestampNs = AREngineExtended.GetBodyTimestamp(i),
                });
            }

            return count;
        }

        /// <summary>
        ///     copies one body's skeleton points, in image space
        /// </summary>
        /// <param name="recordIndex">the body's position in the last GetBodies list</param>
        /// <param name="results">cleared, then filled with pixel coordinates</param>
        /// <returns>how many points were read</returns>
        /// <remarks>
        ///     the AREngine C API exposes 2D skeleton points only, so world-space
        ///     joints have to be reconstructed from these plus the body pose
        /// </remarks>
        public static int CopyBodySkeleton2D(int recordIndex, List<Vector2> results)
        {
            results.Clear();
            var written = AREngineExtended.CopyBodySkeleton2D(recordIndex, FloatBuffer(MaxBodyFloats), MaxBodyFloats);
            if (written < 0)
            {
                return Failed("body skeleton", written);
            }

            for (var i = 0; i + 1 < written; i += 2)
            {
                results.Add(new Vector2(_floats[i], _floats[i + 1]));
            }

            return results.Count;
        }

        /// <summary>
        ///     copies the bone identifier for each skeleton point
        /// </summary>
        /// <param name="recordIndex">the body's position in the last GetBodies list</param>
        /// <param name="results">cleared, then filled with one type per point</param>
        /// <returns>how many types were read</returns>
        public static int CopyBodySkeletonTypes(int recordIndex, List<AREngine_ARBodySkeletonType> results)
        {
            results.Clear();
            var written = AREngineExtended.CopyBodySkeletonTypes(recordIndex, IntBuffer(MaxBodyPoints), MaxBodyPoints);
            if (written < 0)
            {
                return Failed("body skeleton types", written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add((AREngine_ARBodySkeletonType)_ints[i]);
            }

            return written;
        }

        /// <summary>
        ///     copies the detection confidence for each skeleton point
        /// </summary>
        /// <param name="recordIndex">the body's position in the last GetBodies list</param>
        /// <param name="results">cleared, then filled with one confidence per point</param>
        /// <returns>how many values were read</returns>
        public static int CopyBodyConfidence(int recordIndex, List<float> results)
        {
            results.Clear();
            var written = AREngineExtended.CopyBodyConfidence(recordIndex, FloatBuffer(MaxBodyPoints), MaxBodyPoints);
            if (written < 0)
            {
                return Failed("body confidence", written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add(_floats[i]);
            }

            return written;
        }

        /// <summary>
        ///     copies whether each skeleton point was detected this frame
        /// </summary>
        /// <param name="recordIndex">the body's position in the last GetBodies list</param>
        /// <param name="results">cleared, then filled with one flag per point</param>
        /// <returns>how many flags were read</returns>
        public static int CopyBodyPointIsValid(int recordIndex, List<bool> results)
        {
            results.Clear();
            var written = AREngineExtended.CopyBodyPointIsValid(recordIndex, IntBuffer(MaxBodyPoints), MaxBodyPoints);
            if (written < 0)
            {
                return Failed("body point validity", written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add(_ints[i] != 0);
            }

            return written;
        }

        /// <summary>
        ///     copies the bones as pairs of skeleton types
        /// </summary>
        /// <param name="recordIndex">the body's position in the last GetBodies list</param>
        /// <param name="results">cleared, then filled with start and end type per bone</param>
        /// <returns>how many ints were read, twice the bone count</returns>
        public static int CopyBodyConnections(int recordIndex, List<int> results)
        {
            results.Clear();
            var written = AREngineExtended.CopyBodyConnections(recordIndex, IntBuffer(MaxBodyPoints * 2),
                MaxBodyPoints * 2);
            if (written < 0)
            {
                return Failed("body connections", written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add(_ints[i]);
            }

            return written;
        }

        /// <summary>
        ///     reads one body's capture time at full precision
        /// </summary>
        /// <param name="recordIndex">the body's position in the last GetBodies list</param>
        /// <returns>nanoseconds; the record's own timestamp field is truncated to float</returns>
        public static long GetBodyTimestamp(int recordIndex)
        {
            return AREngineExtended.GetBodyTimestamp(recordIndex);
        }

        #endregion

        #region Semantics

        /// <summary>
        ///     reads the object shapes recognised this frame; requires target
        ///     semantic mode
        /// </summary>
        /// <param name="results">cleared, then filled with the shapes</param>
        /// <param name="maxRecords">how many shapes to read at most</param>
        /// <returns>how many shapes were read</returns>
        public static int GetTargets(List<AREngineTarget> results, int maxRecords = DefaultMaxRecords)
        {
            results.Clear();
            var count = AREngineExtended.GetTargets(RecordBuffer(maxRecords), maxRecords);
            if (count < 0)
            {
                return Failed("targets", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats + AREngineX.Target.Aabb;
                var min = new Vector3(_records[o], _records[o + 1], _records[o + 2]);
                var max = new Vector3(_records[o + 3], _records[o + 4], _records[o + 5]);
                var p = i * AREngineX.RecordFloats;
                results.Add(new AREngineTarget
                {
                    centerPose = AREngineRecord.ReadPose(_records, p + AREngineX.Target.Quat),
                    shape = (AREngine_ARTargetShapeLabel)(int)_records[p + AREngineX.Target.Shape],
                    radius = _records[p + AREngineX.Target.Radius],
                    bounds = new Bounds((min + max) * 0.5f, max - min),
                });
            }

            return count;
        }

        /// <summary>
        ///     acquires this frame's dense semantic data
        /// </summary>
        /// <returns>true when data is available to copy</returns>
        public static bool AcquireSemanticDense()
        {
            var status = AREngineExtended.AcquireSemanticDense();
            if (status != AREngine_ARStatus.ARENGINE_SUCCESS)
            {
                Debug.LogWarning($"arengine: dense semantics not available: {GetStatusText(status)}");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     how many semantic points the acquired data holds
        /// </summary>
        public static int SemanticPointCount => AREngineExtended.GetSemanticPointCount();

        /// <summary>
        ///     how many semantic cubes the acquired data holds
        /// </summary>
        public static int SemanticCubeCount => AREngineExtended.GetSemanticCubeCount();

        /// <summary>
        ///     copies the acquired semantic points as raw floats
        /// </summary>
        /// <param name="results">cleared, then filled with 9 floats per point:
        ///     id, x, y, z, r, g, b, a, confidence</param>
        /// <returns>how many floats were read</returns>
        public static int CopySemanticPointData(List<float> results)
        {
            return CopyRawFloats(results, SemanticPointCount * SemanticPointFloats,
                (buffer, max) => AREngineExtended.CopySemanticPointData(buffer, max), "semantic points");
        }

        /// <summary>
        ///     copies the acquired semantic cubes as raw floats
        /// </summary>
        /// <param name="results">cleared, then filled with 28 floats per cube: id,
        ///     vertex count, confidence, label, then 8 vertices as 24 floats</param>
        /// <returns>how many floats were read</returns>
        public static int CopySemanticCubeData(List<float> results)
        {
            return CopyRawFloats(results, SemanticCubeCount * SemanticCubeFloats,
                (buffer, max) => AREngineExtended.CopySemanticCubeData(buffer, max), "semantic cubes");
        }

        /// <summary>
        ///     releases the acquired dense semantic data
        /// </summary>
        public static void ReleaseSemanticDense()
        {
            AREngineExtended.ReleaseSemanticDense();
        }

        #endregion

        #region Misc

        /// <summary>
        ///     turns the torch on
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when the flash is on</returns>
        public static AREngine_ARStatus OpenFlash()
        {
            return ReportStatus("open flash", AREngineExtended.OpenFlash());
        }

        /// <summary>
        ///     turns the torch off
        /// </summary>
        /// <returns>ARENGINE_SUCCESS when the flash is off</returns>
        public static AREngine_ARStatus CloseFlash()
        {
            return ReportStatus("close flash", AREngineExtended.CloseFlash());
        }

        /// <summary>
        ///     the last status the bridge saw, for diagnostics
        /// </summary>
        public static AREngine_ARStatus LastStatus => (AREngine_ARStatus)AREngineExtended.GetLastStatus();

        #endregion

        #region Internals

        // AREngine reports up to 20 skeleton bones, so 24 points leaves room
        // without needing a count query the C API does not offer per record.
        private const int MaxBodyPoints = 24;
        private const int MaxBodyFloats = MaxBodyPoints * 2;

        private static int ReadPlanes(List<AREnginePlane> results, int count)
        {
            results.Clear();
            if (count < 0)
            {
                return Failed("planes", count);
            }

            for (var i = 0; i < count; i++)
            {
                var o = i * AREngineX.RecordFloats;
                results.Add(new AREnginePlane
                {
                    centerPose = AREngineRecord.ReadPose(_records, o + AREngineX.Plane.Quat),
                    extentX = _records[o + AREngineX.Plane.ExtentX],
                    extentZ = _records[o + AREngineX.Plane.ExtentZ],
                    planeType = (AREngine_ARPlaneType)(int)_records[o + AREngineX.Plane.Type],
                    label = (AREngine_ARSemanticPlaneLabel)(int)_records[o + AREngineX.Plane.Label],
                    trackingState = (AREngine_ARTrackingState)(int)_records[o + AREngineX.Plane.State],
                    trackableId = (int)_records[o + AREngineX.Plane.Trackable],
                });
            }

            return count;
        }

        private static string ReadImageName(int recordIndex)
        {
            var written = AREngineExtended.GetTrackedImageName(recordIndex, ByteBuffer(ImageNameChars), ImageNameChars);
            return written <= 0 ? string.Empty : Encoding.UTF8.GetString(_bytes, 0, written);
        }

        private static int CopyTriplets(List<Vector3> results, int count,
            System.Func<float[], int, int> copy, string what)
        {
            results.Clear();
            if (count <= 0)
            {
                return 0;
            }

            var written = copy(FloatBuffer(count * 3), count * 3);
            if (written < 0)
            {
                return Failed(what, written);
            }

            for (var i = 0; i + 2 < written; i += 3)
            {
                results.Add(new Vector3(_floats[i], _floats[i + 1], _floats[i + 2]));
            }

            return results.Count;
        }

        private static int CopyRawFloats(List<float> results, int maxFloats,
            System.Func<float[], int, int> copy, string what)
        {
            results.Clear();
            if (maxFloats <= 0)
            {
                return 0;
            }

            var written = copy(FloatBuffer(maxFloats), maxFloats);
            if (written < 0)
            {
                return Failed(what, written);
            }

            for (var i = 0; i < written; i++)
            {
                results.Add(_floats[i]);
            }

            return written;
        }

        private static int ReportId(string what, int id)
        {
            if (id < 0)
            {
                Debug.LogError($"arengine: error: failed to {what}: {GetStatusText((AREngine_ARStatus)(-id))}");
            }

            return id;
        }

        #endregion
    }
}
