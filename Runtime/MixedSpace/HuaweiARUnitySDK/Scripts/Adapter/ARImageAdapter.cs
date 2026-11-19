namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using AREngineBridge;

    // The CPU-side camera image (YUV_420_888), on top of the AREngineBridge.
    //
    // HarmonyOS 上相机预览纹理是 CreateExternalTexture 创建的 native texture，不能对其调用
    // GetPixels32/GetRawTextureData(会抛 "not allowed on native textures")，必须先
    // AcquireCameraImage 拿到图像，再从 CPU 侧读取像素数据。这条路径没变，变的只是句柄的含义。
    //
    // 桥接层只保留"当前帧的一张图"这一个槽位，而且 TJAREX_AcquireCameraImage 会先释放上一张
    // 再取新的；旧的 native 层是每次 acquire 得到独立引用、每次 release 减一，调用方(HWSupport)
    // 按那个语义在用。为了不留下"句柄还活着、底层图却已被换掉"的悬空指针，这里用引用计数把多个
    // 句柄折叠成桥接层那一张图：第一个句柄真正 acquire，最后一个句柄 release 时才真正释放。
    // 取不到图仍然返回 IntPtr.Zero，调用方的判断照旧。
    internal class ARImageAdapter
    {
        /// <summary>token 表：句柄是否仍然有效</summary>
        private readonly BridgeTokenTable<bool> m_Images = new BridgeTokenTable<bool>();

        /// <summary>当前有多少个句柄指向桥接层那一张图</summary>
        private int m_LiveCount;

        /// <summary>
        ///     the geometry read once per image, so Width, Height and the plane
        ///     strides are guaranteed to describe the same image
        /// </summary>
        private int m_Width;
        private int m_Height;
        private int m_Format;
        private int m_PlaneCount;

        /// <summary>采集可能在别的线程上跑，计数和槽位表要一起保护</summary>
        private readonly object m_Lock = new object();

        public ARImageAdapter(NDKSession session)
        {
            // The image belongs to the session's current frame, which the bridge
            // reaches through its singleton, so this adapter has no use for the
            // NDKSession it is filed under. The parameter stays because NDKSession
            // constructs every adapter the same way.
        }

        /// <summary>
        ///     takes this frame's image for CPU access
        /// </summary>
        /// <returns>a handle, or IntPtr.Zero when the frame has no image</returns>
        public IntPtr Acquire()
        {
            lock (m_Lock)
            {
                if (m_LiveCount == 0 && !AREngineSession.AcquireCameraImage())
                {
                    return IntPtr.Zero;
                }

                if (m_LiveCount == 0)
                {
                    AREngineSession.TryGetCameraImageInfo(out m_Width, out m_Height, out m_Format,
                        out m_PlaneCount);
                }

                m_LiveCount++;
                return m_Images.Store(true);
            }
        }

        public void Release(IntPtr imageHandle)
        {
            lock (m_Lock)
            {
                if (!IsLive(imageHandle))
                {
                    // 已经释放过，或者是个来自上一张图的旧句柄：桥接层那张图不该被动到。
                    return;
                }

                m_Images.Release(imageHandle);
                m_LiveCount--;
                if (m_LiveCount > 0)
                {
                    return;
                }

                m_LiveCount = 0;
                m_Width = 0;
                m_Height = 0;
                m_Format = 0;
                m_PlaneCount = 0;
                AREngineSession.ReleaseCameraImage();
            }
        }

        public int GetWidth(IntPtr imageHandle)
        {
            lock (m_Lock)
            {
                return IsLive(imageHandle) ? m_Width : 0;
            }
        }

        public int GetHeight(IntPtr imageHandle)
        {
            lock (m_Lock)
            {
                return IsLive(imageHandle) ? m_Height : 0;
            }
        }

        /// <summary>format of the image behind this handle, as an AREngine_ARImageFormat value</summary>
        public int GetFormat(IntPtr imageHandle)
        {
            lock (m_Lock)
            {
                return IsLive(imageHandle) ? m_Format : 0;
            }
        }

        /// <summary>how many planes the image behind this handle has</summary>
        public int GetPlaneCount(IntPtr imageHandle)
        {
            lock (m_Lock)
            {
                return IsLive(imageHandle) ? m_PlaneCount : 0;
            }
        }

        /// <summary>
        ///     borrows one plane of the acquired image straight out of the SDK
        /// </summary>
        /// <remarks>
        ///     the pointer is the SDK's own, valid only until the last handle is
        ///     released - which is what it was through the old native layer, where
        ///     HWSupport walks it row by row with Marshal.Copy and never keeps it.
        /// </remarks>
        public IntPtr GetPlaneData(IntPtr imageHandle, int planeIdx, out int dataLength)
        {
            int rowStride;
            int pixelStride;
            return GetPlane(imageHandle, planeIdx, out rowStride, out pixelStride, out dataLength);
        }

        public int GetPlaneRowStride(IntPtr imageHandle, int planeIdx)
        {
            int pixelStride;
            int dataLength;
            int rowStride;
            GetPlane(imageHandle, planeIdx, out rowStride, out pixelStride, out dataLength);
            return rowStride;
        }

        public int GetPlanePixelStride(IntPtr imageHandle, int planeIdx)
        {
            int rowStride;
            int dataLength;
            int pixelStride;
            GetPlane(imageHandle, planeIdx, out rowStride, out pixelStride, out dataLength);
            return pixelStride;
        }

        /// <summary>the one bridge call every plane accessor above is a view of</summary>
        private IntPtr GetPlane(IntPtr imageHandle, int planeIdx, out int rowStride, out int pixelStride,
            out int dataLength)
        {
            rowStride = 0;
            pixelStride = 0;
            dataLength = 0;
            lock (m_Lock)
            {
                if (!IsLive(imageHandle))
                {
                    return IntPtr.Zero;
                }
            }

            return AREngineSession.GetCameraImagePlaneData(planeIdx, out rowStride, out pixelStride, out dataLength);
        }

        /// <summary>does this handle still name the image the bridge is holding; caller holds m_Lock</summary>
        private bool IsLive(IntPtr imageHandle)
        {
            bool ignored;
            return m_Images.TryGet(imageHandle, out ignored);
        }
    }
}
