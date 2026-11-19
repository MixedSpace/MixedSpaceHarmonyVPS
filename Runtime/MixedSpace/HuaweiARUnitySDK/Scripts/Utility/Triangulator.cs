using UnityEngine;
using System.Collections.Generic;

namespace HuaweiARUnitySDK
{
    //this class is used to calcluate the index of vertices in Concave polygons
    public class Triangulator
    {
        private List<Vector2> m_points;

        //input points is in unity plane local coordinate, and the order is x z
        public Triangulator(List<Vector2> points)
        {
            m_points = points;
        }

        public int[] Triangulate()
        {
            List<int> indices = new List<int>();
            int pointCount = m_points.Count;

            if (pointCount < 3)
                return indices.ToArray();

            int[] vertexIndices = InitializeVertexIndices(pointCount);
            int remainingCount = pointCount;
            int iterationLimit = 2 * remainingCount;

            for (int m = 0, current = remainingCount - 1; remainingCount > 2;)
            {
                if (iterationLimit-- <= 0)
                    return indices.ToArray();

                int u = GetWrappedIndex(current, remainingCount);
                current = GetNextIndex(u, remainingCount);
                int v = current;
                int w = GetNextIndex(v, remainingCount);

                if (Snip(u, v, w, remainingCount, vertexIndices))
                {
                    AddTriangleIndices(indices, vertexIndices, u, v, w);

                    RemoveVertexAt(ref vertexIndices, v, ref remainingCount);
                    iterationLimit = 2 * remainingCount;
                    m++;
                }
            }

            indices.Reverse();
            return indices.ToArray();
        }

        // 初始化顶点索引数组
        private int[] InitializeVertexIndices(int pointCount)
        {
            int[] indices = new int[pointCount];
            bool isPositiveArea = Area() > 0;

            for (int i = 0; i < pointCount; i++)
                indices[i] = isPositiveArea ? i : (pointCount - 1) - i;

            return indices;
        }

        // 获取循环索引
        private int GetWrappedIndex(int index, int count) => index >= count ? 0 : index;
        private int GetNextIndex(int index, int count) => GetWrappedIndex(index + 1, count);

        // 添加三角形索引
        private void AddTriangleIndices(List<int> indices, int[] vertexIndices, int u, int v, int w)
        {
            indices.Add(vertexIndices[u]);
            indices.Add(vertexIndices[v]);
            indices.Add(vertexIndices[w]);
        }

        // 移除已剖分的顶点
        private void RemoveVertexAt(ref int[] array, int index, ref int count)
        {
            for (int t = index; t < count - 1; t++)
                array[t] = array[t + 1];
            count--;
        }

        private float Area()
        {
            int n = m_points.Count;
            float A = 0.0f;
            for (int p = n - 1, q = 0; q < n; p = q++)
            {
                Vector2 pval = m_points[p];
                Vector2 qval = m_points[q];
                A += pval.x * qval.y - qval.x * pval.y;
            }
            return (A * 0.5f);
        }

        private bool Snip(int u, int v, int w, int n, int[] V)
        {
            int p;
            Vector2 A = m_points[V[u]];
            Vector2 B = m_points[V[v]];
            Vector2 C = m_points[V[w]];
            if (Mathf.Epsilon > (((B.x - A.x) * (C.y - A.y)) - ((B.y - A.y) * (C.x - A.x))))
                return false;
            for (p = 0; p < n; p++)
            {
                if ((p == u) || (p == v) || (p == w))
                    continue;
                Vector2 P = m_points[V[p]];
                if (InsideTriangle(A, B, C, P))
                    return false;
            }
            return true;
        }

        private bool InsideTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 P)
        {
            float ax, ay, bx, by, cx, cy, apx, apy, bpx, bpy, cpx, cpy;
            float cCROSSap, bCROSScp, aCROSSbp;

            ax = C.x - B.x;
            ay = C.y - B.y;
            bx = A.x - C.x;
            by = A.y - C.y;
            cx = B.x - A.x;
            cy = B.y - A.y;
            apx = P.x - A.x;
            apy = P.y - A.y;
            bpx = P.x - B.x;
            bpy = P.y - B.y;
            cpx = P.x - C.x;
            cpy = P.y - C.y;

            aCROSSbp = ax * bpy - ay * bpx;
            cCROSSap = cx * apy - cy * apx;
            bCROSScp = bx * cpy - by * cpx;

            return ((aCROSSbp >= 0.0f) && (bCROSScp >= 0.0f) && (cCROSSap >= 0.0f));
        }
    }
}