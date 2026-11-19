namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using AREngineBridge;

    // Dense semantics (稠密语义点云与体积测量), on top of the AREngineBridge.
    //
    // The old layer acquired a native ARSemanticDense object, borrowed two raw
    // pointers out of it, filled the caller's arrays and released the object again,
    // all inside one call. The bridge copies the same data as flat records - nine
    // floats per point, twenty-eight per cube - so this adapter stays a one-shot read
    // too, and nothing needs a handle.
    //
    // What the bridge does not do is convert handedness, so Z is negated here exactly
    // as the old adapter negated it.
    internal class ARSemanticDenseAdapter
    {
        /// <summary>id, x, y, z, r, g, b, a, confidence</summary>
        private const int PointFloats = 9;

        /// <summary>id, vertex count, confidence, label, then eight corners</summary>
        private const int CubeFloats = 28;

        private const int CubeVertexFloats = 4;

        private const int CubeVertexCount = 8;

        private readonly List<float> m_Points = new List<float>();
        private readonly List<float> m_Cubes = new List<float>();

        public ARSemanticDenseAdapter(NDKSession session)
        {
            // The dense data belongs to the bridge's session singleton. The parameter
            // stays because NDKSession constructs every adapter the same way.
        }

        public void GetSemanticDenseData(ref Vector3[] vertices, ref Color[] colors, ref int size)
        {
            if (!AREngineSession.AcquireSemanticDense())
            {
                ARDebug.LogError("AcquireSemanticDenseData is error, status:{0}", AREngineSession.LastStatus);
                return;
            }

            int written = AREngineSession.CopySemanticPointData(m_Points);
            AREngineSession.ReleaseSemanticDense();
            if (written < PointFloats)
            {
                return;
            }

            size = written / PointFloats;
            vertices = new Vector3[size];
            colors = new Color[size];
            for (int i = 0; i < size; i++)
            {
                int record = i * PointFloats;
                vertices[i].x = m_Points[record + 1];
                vertices[i].y = m_Points[record + 2];
                vertices[i].z = -m_Points[record + 3];
                colors[i].r = m_Points[record + 4] / 255.0f;
                colors[i].g = m_Points[record + 5] / 255.0f;
                colors[i].b = m_Points[record + 6] / 255.0f;

                // Kept from the old adapter, quirk and all: the alpha channel is
                // subtracted from one as a raw 0..255 value rather than being divided
                // like the other three. The point sample draws with MeshTopology.Points
                // and ignores alpha, so nothing noticed; if the semantics ever have to
                // blend, this is the line to revisit.
                colors[i].a = 1 - m_Points[record + 7];
            }
        }

        public void GetVolumeSpaceMeasureData(ref Vector3[] vertices)
        {
            if (!AREngineSession.AcquireSemanticDense())
            {
                ARDebug.LogError("AcquireSemanticDenseData is error, status:{0}", AREngineSession.LastStatus);
                return;
            }

            int written = AREngineSession.CopySemanticCubeData(m_Cubes);
            AREngineSession.ReleaseSemanticDense();
            if (written < CubeFloats)
            {
                return;
            }

            // The old code read the vertex count out of the first cube and then walked
            // that many Vector3s off a flat pointer, which is the cube's eight corners
            // - what VolumeMeasureController draws its bounding box from. Clamp to what
            // one record actually holds so a device that reports more cannot read the
            // next cube's header as a position.
            int count = (int)m_Cubes[1];
            if (count <= 0 || count > CubeVertexCount)
            {
                count = CubeVertexCount;
            }

            vertices = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int record = CubeVertexFloats + i * 3;
                vertices[i].x = m_Cubes[record];
                vertices[i].y = m_Cubes[record + 1];
                vertices[i].z = -m_Cubes[record + 2];
            }
        }
    }
}
