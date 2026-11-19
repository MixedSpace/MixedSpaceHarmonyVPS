namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using AREngineBridge;

    // The environment mesh (场景 Mesh), on top of the AREngineBridge.
    //
    // The old layer handed out a native ARSceneMesh per acquire and read it lazily,
    // which the SDK's own ARSceneMesh façade leans on hard: it keeps the handle in a
    // field and releases it from a finalizer, and SceneMeshVisualizer overwrites its
    // reference every frame without releasing, so a mesh can be read long after the
    // frame that produced it and released on whichever thread the GC prefers.
    //
    // The bridge has one scene mesh slot and TJAREX_AcquireSceneMesh drops the
    // previous one, so lazy reads through a handle would be reading whatever the
    // newest frame put there - or nothing, once the native object is gone. This
    // adapter therefore copies the mesh into managed arrays at acquire time and hands
    // out a token for that snapshot, which keeps every façade behaviour intact:
    // reading is valid for as long as the ARSceneMesh object is alive, a late or
    // duplicated release only drops a table slot, and the native mesh is released
    // straight after the copy instead of being held across frames.
    //
    // The conversion is the one the old code did in C#: the bridge reports AREngine's
    // right-handed vertices, so Z is negated and each triangle's winding is reversed
    // to suit Unity's left-handed space. Normals are left as they came, because that
    // is what the old adapter did.
    internal class ARSceneMeshAdapter
    {
        /// <summary>one acquired mesh, already converted to Unity space</summary>
        private struct SceneMeshSnapshot
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public int[] Indices;
        }

        private readonly BridgeTokenTable<SceneMeshSnapshot> m_Meshes =
            new BridgeTokenTable<SceneMeshSnapshot>();

        /// <summary>scratch lists, so an acquire costs one set of result arrays rather than three</summary>
        private readonly List<Vector3> m_PositionScratch = new List<Vector3>();
        private readonly List<int> m_IndexScratch = new List<int>();

        public ARSceneMeshAdapter(NDKSession session)
        {
            // The mesh is the session's current frame's, which the bridge reaches
            // through its singleton. The parameter stays because NDKSession
            // constructs every adapter the same way.
        }

        /// <summary>
        ///     copies this frame's mesh out of the bridge
        /// </summary>
        /// <returns>a handle, or IntPtr.Zero when there is no mesh to read</returns>
        public IntPtr Acquire()
        {
            if (!AREngineSession.AcquireSceneMesh())
            {
                return IntPtr.Zero;
            }

            SceneMeshSnapshot snapshot = new SceneMeshSnapshot();
            snapshot.Vertices = ReadVertices();
            snapshot.Normals = ReadNormals();
            snapshot.Indices = ReadIndices();

            // Copied, so the bridge can drop its reference now rather than waiting
            // for a finalizer that may never run in the order the frames came in.
            AREngineSession.ReleaseSceneMesh();
            return m_Meshes.Store(snapshot);
        }

        public void Release(IntPtr sceneMeshHandle)
        {
            m_Meshes.Release(sceneMeshHandle);
        }

        public Vector3[] GetVertices(IntPtr sceneMeshHandle)
        {
            SceneMeshSnapshot snapshot;
            return m_Meshes.TryGet(sceneMeshHandle, out snapshot) ? snapshot.Vertices : new Vector3[0];
        }

        public Vector3[] GetVertexNormals(IntPtr sceneMeshHandle)
        {
            SceneMeshSnapshot snapshot;
            return m_Meshes.TryGet(sceneMeshHandle, out snapshot) ? snapshot.Normals : new Vector3[0];
        }

        public int[] GetTriangleIndex(IntPtr sceneMeshHandle)
        {
            SceneMeshSnapshot snapshot;
            return m_Meshes.TryGet(sceneMeshHandle, out snapshot) ? snapshot.Indices : new int[0];
        }

        private Vector3[] ReadVertices()
        {
            int written = AREngineSession.CopySceneMeshVertices(m_PositionScratch);
            if (written <= 0)
            {
                return new Vector3[0];
            }

            Vector3[] result = new Vector3[written];
            for (int i = 0; i < written; i++)
            {
                Vector3 glVertex = m_PositionScratch[i];
                result[i] = new Vector3(glVertex.x, glVertex.y, -glVertex.z);
            }
            return result;
        }

        private Vector3[] ReadNormals()
        {
            int written = AREngineSession.CopySceneMeshNormals(m_PositionScratch);
            if (written <= 0)
            {
                return new Vector3[0];
            }

            // Not flipped: a normal is a direction rather than a position, and the
            // old adapter passed these straight through, so the mesh shades the same
            // way it always did.
            Vector3[] result = new Vector3[written];
            for (int i = 0; i < written; i++)
            {
                result[i] = m_PositionScratch[i];
            }
            return result;
        }

        private int[] ReadIndices()
        {
            int written = AREngineSession.CopySceneMeshIndices(m_IndexScratch);
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

            // A native index count that is not a multiple of three would have run the
            // old loop off the end of both arrays; copy the leftover entries through
            // instead of guessing at a partial triangle.
            int wholeTriangles = written / 3 * 3;
            for (int i = wholeTriangles; i < written; i++)
            {
                result[i] = m_IndexScratch[i];
            }
            return result;
        }
    }
}
