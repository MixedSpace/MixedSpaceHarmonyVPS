namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    // The face mesh, on top of the AREngineBridge.
    //
    // Same shape as the scene mesh adapter, for the same reason: ARFaceGeometry
    // keeps its handle in a field, reads it lazily and releases it from a finalizer,
    // while the bridge has one face geometry slot that the next acquire overwrites.
    // So the mesh is copied into managed arrays while the bridge's copy is live, the
    // native object is released before Acquire returns, and the handle is a token
    // into that snapshot. Late or duplicated releases then cost nothing.
    //
    // The conversion is the one the old adapter did in C#: AREngine's vertices are
    // right-handed, so Z is negated and every triangle's winding is reversed. Texture
    // coordinates pass through unchanged.
    internal class ARFaceGeometryAdapter
    {
        /// <summary>one acquired face mesh, already converted to Unity space</summary>
        private struct FaceGeometrySnapshot
        {
            public Vector3[] Vertices;
            public Vector2[] TexCoords;
            public int[] Indices;
        }

        private readonly BridgeTokenTable<FaceGeometrySnapshot> m_Geometries =
            new BridgeTokenTable<FaceGeometrySnapshot>();

        /// <summary>scratch buffers, so acquiring a mesh allocates only the results</summary>
        private readonly List<Vector3> m_VertexScratch = new List<Vector3>();
        private readonly List<Vector2> m_TexCoordScratch = new List<Vector2>();
        private readonly List<int> m_IndexScratch = new List<int>();

        public ARFaceGeometryAdapter(NDKSession session)
        {
            // The geometry belongs to the bridge's session singleton. The parameter
            // stays because NDKSession constructs every adapter the same way.
        }

        /// <summary>
        ///     copies the face mesh of one tracked face
        /// </summary>
        /// <param name="trackableId">the face's session-stable id</param>
        /// <returns>a handle, or IntPtr.Zero when the face has no mesh</returns>
        public IntPtr Acquire(int trackableId)
        {
            if (!AREngineSession.AcquireFaceGeometryById(trackableId))
            {
                return IntPtr.Zero;
            }

            FaceGeometrySnapshot snapshot = new FaceGeometrySnapshot();
            snapshot.Vertices = ReadVertices();
            snapshot.TexCoords = ReadTexCoords();
            snapshot.Indices = ReadIndices();
            AREngineSession.ReleaseFaceGeometry();

            return m_Geometries.Store(snapshot);
        }

        public void Release(IntPtr geometryHandle)
        {
            m_Geometries.Release(geometryHandle);
        }

        public int GetTriangleCount(IntPtr geometryHandle)
        {
            FaceGeometrySnapshot snapshot;
            if (!m_Geometries.TryGet(geometryHandle, out snapshot) || snapshot.Indices == null)
            {
                return 0;
            }

            // The old native call counted triangles; the bridge counts indices.
            return snapshot.Indices.Length / 3;
        }

        public Vector3[] GetVertices(IntPtr geometryHandle)
        {
            FaceGeometrySnapshot snapshot;
            return m_Geometries.TryGet(geometryHandle, out snapshot) ? snapshot.Vertices : new Vector3[0];
        }

        public Vector2[] GetTexCoord(IntPtr geometryHandle)
        {
            FaceGeometrySnapshot snapshot;
            return m_Geometries.TryGet(geometryHandle, out snapshot) ? snapshot.TexCoords : new Vector2[0];
        }

        public int[] GetTriangleIndex(IntPtr geometryHandle)
        {
            FaceGeometrySnapshot snapshot;
            return m_Geometries.TryGet(geometryHandle, out snapshot) ? snapshot.Indices : new int[0];
        }

        public ARFaceGeometry.Label[] GetTriangleLabels(IntPtr geometryHandle)
        {
            // AREngine does expose ARFaceGeometry_AcquireTriangleLabels, but only
            // through a geometry pointer, and the bridge's face mesh surface is
            // copy-out-by-id rather than a borrowed object, so there is nothing here
            // to read them from. The face samples in this project do not use per
            // triangle materials, so this stays a reported gap; closing it means
            // adding TJAREX_CopyFaceTriangleLabels to the native bridge, which also
            // means a version bump and a rebuilt .so.
            ARBridge.NotSupported("ARFaceGeometry.TriangleLabels");
            return new ARFaceGeometry.Label[0];
        }

        private Vector3[] ReadVertices()
        {
            int written = AREngineSession.CopyFaceVertices(m_VertexScratch);
            if (written <= 0)
            {
                return new Vector3[0];
            }

            Vector3[] result = new Vector3[written];
            for (int i = 0; i < written; i++)
            {
                Vector3 glVertex = m_VertexScratch[i];
                result[i] = new Vector3(glVertex.x, glVertex.y, -glVertex.z);
            }
            return result;
        }

        private Vector2[] ReadTexCoords()
        {
            int written = AREngineSession.CopyFaceTexCoords(m_TexCoordScratch);
            if (written <= 0)
            {
                return new Vector2[0];
            }

            Vector2[] result = new Vector2[written];
            m_TexCoordScratch.CopyTo(0, result, 0, written);
            return result;
        }

        private int[] ReadIndices()
        {
            int written = AREngineSession.CopyFaceIndices(m_IndexScratch);
            if (written <= 0)
            {
                return new int[0];
            }

            int[] result = new int[written];
            for (int i = 0; i + 2 < written; i += 3)
            {
                result[i] = m_IndexScratch[i + 2];
                result[i + 1] = m_IndexScratch[i + 1];
                result[i + 2] = m_IndexScratch[i];
            }

            int wholeTriangles = written / 3 * 3;
            for (int i = wholeTriangles; i < written; i++)
            {
                result[i] = m_IndexScratch[i];
            }
            return result;
        }
    }
}
