using UnityEngine;

namespace MixedSpace.XR
{
    /// <summary>
    /// Converts values across the Unity/native coordinate boundary. All operations
    /// return a new value so a conversion cannot silently mutate its caller.
    /// </summary>
    public static class MixedSpaceCoordinateFrame
    {
        public static Matrix4x4 ReflectXAxis(Matrix4x4 pose)
        {
            // D * pose * D where D = diag(-1, 1, 1, 1). Writing the
            // affected components directly keeps this boundary deterministic
            // and independent of Unity's native matrix implementation.
            pose.m01 = -pose.m01;
            pose.m02 = -pose.m02;
            pose.m03 = -pose.m03;
            pose.m10 = -pose.m10;
            pose.m20 = -pose.m20;
            pose.m30 = -pose.m30;
            return pose;
        }

        public static Vector3 ReflectXAxis(Vector3 position)
        {
            return new Vector3(-position.x, position.y, position.z);
        }

        public static Quaternion ReflectXAxis(Quaternion rotation)
        {
            // Keep Unity's matrix-to-rotation extraction for non-unit inputs
            // and quaternion sign selection, as used by shipped map poses.
            // Direct component negation is equivalent only for valid rotations.
            return ReflectXAxis(Matrix4x4.Rotate(rotation)).rotation;
        }

        public static float[] CopyQuaternionComponents(Quaternion rotation)
        {
            return new[] { rotation.x, rotation.y, rotation.z, rotation.w };
        }

        /// <summary>
        /// Builds the row-major 3x3 rotation expected by the map/native boundary.
        /// MixedSpace alignment quaternions are stored in XYZW fields.
        /// </summary>
        public static double[] BuildAlignmentRotation(MixedSpaceMapAlignment alignment)
        {
            double w = alignment.qw;
            double x = alignment.qx;
            double y = alignment.qy;
            double z = alignment.qz;
            double ww = w * w;
            double xx = x * x;
            double yy = y * y;
            double zz = z * z;
            double xy = x * y;
            double zw = z * w;
            double xz = x * z;
            double yw = y * w;
            double yz = y * z;
            double xw = x * w;
            double inverseLengthSquared = 1.0 / (xx + yy + zz + ww);

            return new[]
            {
                (xx - yy - zz + ww) * inverseLengthSquared,
                2.0 * (xy - zw) * inverseLengthSquared,
                2.0 * (xz + yw) * inverseLengthSquared,
                2.0 * (xy + zw) * inverseLengthSquared,
                (-xx + yy - zz + ww) * inverseLengthSquared,
                2.0 * (yz - xw) * inverseLengthSquared,
                2.0 * (xz - yw) * inverseLengthSquared,
                2.0 * (yz + xw) * inverseLengthSquared,
                (-xx - yy + zz + ww) * inverseLengthSquared
            };
        }
    }
}
