namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using HuaweiARUnitySDK;
    using AREngineBridge;

    internal class ARUnityHelper
    {
        private int m_textureId = -1;
        private static ARUnityHelper s_unityHelper;

        private  ARUnityHelper()
        {
        }
        
        public static ARUnityHelper Instance
        {
            get
            {
                if (null == s_unityHelper)
                {
                    s_unityHelper= new ARUnityHelper();
                }
                return s_unityHelper;
            }
        }


        public IntPtr GetJEnv()
        {
            return IntPtr.Zero;
        }

        public IntPtr GetActivityHandle()
        { 
            return IntPtr.Zero;
        }

        public int GetTextureId()
        {
            // 相机 OES 纹理现在归桥接层所有：它在渲染线程上 glGenTextures（那里才有
            // 引擎的 current EGL context），并在 DestroySession 时连会话一起删掉。
            // 所以只能以桥接层当前的纹理 id 为准：会话销毁后纹理 id 会回到 0，
            // 若继续返回缓存里的旧 id，SetCameraTextureName 就会去绑一个已经不存在的纹理。
            uint liveTextureId = AREngineSession.CameraTextureId;
            if (liveTextureId != 0)
            {
                m_textureId = (int)liveTextureId;
                return m_textureId;
            }

            // m_textureId <= 0 表示尚未成功创建（-1 为初始值，0 为上次创建失败），需要重试。
            // InitialiseCameraTexture 会在渲染线程上创建纹理并绑到会话上，
            // 返回 0 多半是当前线程没有 current GL context（例如在权限异步回调的线程上调用）。
            m_textureId = (int)AREngineSession.InitialiseCameraTexture();
            if (m_textureId <= 0)
            {
                UnityEngine.Debug.LogWarning("[GetTextureId] 桥接层创建相机纹理失败, textureId=" + m_textureId
                    + ", sessionCreated=" + AREngineSession.IsCreated
                    + ", threadId=" + System.Threading.Thread.CurrentThread.ManagedThreadId
                    + "（GL 上下文可能不在当前线程，下一帧重试）");
            }
            return m_textureId;
        }
    }
}
