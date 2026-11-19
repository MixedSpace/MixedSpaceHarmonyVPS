namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    // Face blend shapes, on top of the AREngineBridge.
    //
    // The old layer acquired a native ARFaceBlendShapes object, walked its two
    // parallel arrays and released it again, all inside ARFace.GetBlendShape(). The
    // bridge copies both arrays in one call by face id, so the weights and the shape
    // types are read together here and the result is stored as the dictionary the
    // façade returns anyway. The handle therefore names a managed dictionary rather
    // than a native object, and Release is what drops it.
    //
    // The two enums agree value for value - both start at eye blink left and run to
    // 64 entries - so a face's shape type casts straight into the SDK's own
    // BlendShapeLocation, which is what the old code did with the int it read.
    internal class ARFaceBlendShapeAdapter
    {
        /// <summary>AREngine reports one weight per animoji blend shape</summary>
        private const int MaxBlendShapes = 64;

        private readonly BridgeTokenTable<Dictionary<ARFace.BlendShapeLocation, float>> m_BlendShapes =
            new BridgeTokenTable<Dictionary<ARFace.BlendShapeLocation, float>>();

        private readonly List<float> m_Weights = new List<float>();
        private readonly List<AREngine_ARAnimojiBlendShape> m_Types =
            new List<AREngine_ARAnimojiBlendShape>();

        public ARFaceBlendShapeAdapter(NDKSession session)
        {
            // The shapes belong to the bridge's session singleton. The parameter
            // stays because NDKSession constructs every adapter the same way.
        }

        /// <summary>
        ///     reads one face's blend shapes
        /// </summary>
        /// <param name="trackableId">the face's session-stable id</param>
        /// <returns>a handle, or IntPtr.Zero when nothing was reported</returns>
        public IntPtr Acquire(int trackableId)
        {
            int written = AREngineSession.CopyFaceBlendShapesById(trackableId, m_Weights, m_Types, MaxBlendShapes);
            if (written <= 0)
            {
                return IntPtr.Zero;
            }

            Dictionary<ARFace.BlendShapeLocation, float> data = new Dictionary<ARFace.BlendShapeLocation, float>();
            for (int i = 0; i < written; i++)
            {
                // The indexer rather than the old Add(): a device that repeats a   
                // shape type would have thrown an ArgumentException out of
                // GetBlendShape() and taken the frame's update with it.
                data[(ARFace.BlendShapeLocation)(int)m_Types[i]] = m_Weights[i];
            }

            return m_BlendShapes.Store(data);
        }

        public void Release(IntPtr blendShapesHandle)
        {
            m_BlendShapes.Release(blendShapesHandle);
        }

        public Dictionary<ARFace.BlendShapeLocation, float> GetBlendShapeData(IntPtr blendShapesHandle)
        {
            Dictionary<ARFace.BlendShapeLocation, float> data;
            if (!m_BlendShapes.TryGet(blendShapesHandle, out data) || data == null)
            {
                return new Dictionary<ARFace.BlendShapeLocation, float>();
            }
            return data;
        }
    }
}
