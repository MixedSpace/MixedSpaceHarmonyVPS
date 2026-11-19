namespace HuaweiARInternal
{
    using HuaweiARUnitySDK;
    using UnityEngine;
    using System;
    using System.Collections.Generic;
    internal class ARSessionManager
    {
        private static ARSessionManager m_sessionManager;
        public NDKSession m_ndkSession;

        internal ARSessionStatus SessionStatus { get; private set; }
        internal bool DisplayGeometrySet = false;

        public Texture2D BackgroundTexture { get; private set; }

        private ARSessionManager() { }

        public static ARSessionManager Instance
        {
            get
            {
                if (m_sessionManager == null)
                {
                    m_sessionManager = new ARSessionManager();
                    m_sessionManager.m_ndkSession = null;
                    m_sessionManager.SessionStatus = ARSessionStatus.STOPPED;
                }
                return m_sessionManager;
            }
        }

        public bool IsCreated()
        {
            return m_ndkSession != null && SessionStatus != ARSessionStatus.STOPPED;
        }

        public void CreateSession()
        {
            // 幂等保护：会话已存在则直接复用，绝不重复 new NDKSession()。
            // 重复 new 会让前一个 NDKSession 失去引用 → GC 在 Finalizer 线程执行 ~NDKSession()
            // → SessionAdapter.Destroy() → HwArSession_destroy 销毁 native 会话。
            // 华为 AREngine 是单会话语义，销毁旧会话会连带拆掉共享的相机/传感器/SLAM 通道
            // (日志表现：grav:0、hsdk hal data queue is empty、slam display tracking state 0)，
            // 而此时 Camera/SLAM/BackgroundRenderer/LocalizeImage 仍在运行，
            // 最终在 OS_FFRT 线程访问悬空 native 指针触发 SIGSEGV(SEGV_MAPERR)。
            // 真机日志实测：CreateSession 在 58ms 内被调用两次，第二次后约 150ms 旧会话即被 Finalize 销毁。
            if (m_ndkSession != null)
            {
                ARDebug.LogWarning("CreateSession ignored: NDKSession already exists. Re-creating would orphan " +
                    "the old session and trigger its Finalizer -> HwArSession_destroy while it is still in use.");
                return;
            }
            m_ndkSession = new NDKSession();
            SessionStatus = ARSessionStatus.INIT;
        }
        public void CreateFrame()
        {
            m_ndkSession.CreateFrame();
        }

        public Matrix4x4 GetProjectionMatrix(float nearClipPlane, float farClipPlane)
        {
            if (IntPtr.Zero == m_ndkSession.FrameHandle)
            {
                return Matrix4x4.identity;
            }
            IntPtr cameraPtr = m_ndkSession.FrameAdapter.AcquireCameraHandle();
            Matrix4x4 projectionMatrix = m_ndkSession.CameraAdapter.GetProjectionMatrix(cameraPtr, nearClipPlane,
                farClipPlane);
            m_ndkSession.CameraAdapter.Release(cameraPtr);
            return projectionMatrix;
        }

        public void SetDisplayGeometry(int width, int height)
        {
            if (ARSessionStatus.STOPPED == SessionStatus)
            {
                ARDebug.LogWarning("Session is stopped when SetDisplayGeometry, ignore it");
                return;
            }
            m_ndkSession.SessionAdapter.SetDisplayGeometry(Screen.orientation, width, height);
            DisplayGeometrySet = true;
        }

        public void SetDisplayGeometry(ScreenOrientation orientation, int width, int height)
        {
            if (ARSessionStatus.STOPPED == SessionStatus)
            {
                ARDebug.LogWarning("Session is stopped when SetDisplayGeometry, ignore it");
                return;
            }
            m_ndkSession.SessionAdapter.SetDisplayGeometry(orientation, width, height);
            DisplayGeometrySet = true;
        }

        public void Resume()
        {
            if (ARSessionStatus.STOPPED == SessionStatus)
            {
                ARDebug.LogWarning("Session is stopped when resume, ignore it");
                return;
            }

            m_ndkSession.SessionAdapter.Resume();
            SessionStatus = ARSessionStatus.RESUMED;
        }

        public void Pause()
        {
            if (ARSessionStatus.STOPPED == SessionStatus)
            {
                ARDebug.LogWarning("session is stopped when pause, ignore it");
                return;
            }
            m_ndkSession.SessionAdapter.Pause();
            SessionStatus = ARSessionStatus.PAUSED;
        }


        public void Stop()
        {
            if (ARSessionStatus.STOPPED == SessionStatus)
            {
                return;
            }
            m_ndkSession.SessionAdapter.Stop();
            SessionStatus = ARSessionStatus.STOPPED;
        }

        public void SetCameraTextureName()
        {
            int id = ARUnityHelper.Instance.GetTextureId();
            if (id <= 0)
            {
                // 防御：native 纹理 id 无效（通常为 0），多因当前线程无 current EGL context 导致 glGenTextures 失败。
                // 若继续执行 CreateExternalTexture(IntPtr.Zero) 会抛 "nativeTex can not be null"，
                // 从而中断 _ConnectToService()，使 ARSession.Resume() 得不到执行、会话停在 INIT。
                // 这里提前返回，保证会话能 Resume 到 RUNNING，纹理留待后续帧重试创建。
                ARDebug.LogError("SetCameraTextureName: invalid native texture id=" + id
                    + ", skip CreateExternalTexture to keep session resumable (GL context not current on this thread?).");
                return;
            }
            m_ndkSession.SessionAdapter.SetCameraTextureName(id);
            if (BackgroundTexture == null)
            {
                BackgroundTexture = Texture2D.CreateExternalTexture(0, 0, TextureFormat.ARGB32, false,
                    false, new IntPtr(id));
                return;
            }
            BackgroundTexture.UpdateExternalTexture(new IntPtr(id));
        }
        public void Update()
        {
            if (SessionStatus != ARSessionStatus.RESUMED &&
                SessionStatus != ARSessionStatus.RUNNING)
            {
                ARDebug.LogWarning("update when state is not resumed or running, ignore it  " + SessionStatus);
                return;
            }
            m_ndkSession.SessionAdapter.Update();
            SessionStatus = ARSessionStatus.RUNNING;
        }

        public ARAnchor AddAnchor(Pose pose)
        {
            if (ARSessionStatus.RUNNING != Instance.SessionStatus)
            {
                ARDebug.LogWarning("add anchor when session is not running, ignore it");
                throw new ARNotYetAvailableException();
            }
            return Instance.m_ndkSession.SessionAdapter.CreateAnchor(pose);
        }
        public void Config(ARConfigBase config)
        {
            if (ARSessionStatus.PAUSED != Instance.SessionStatus &&
                ARSessionStatus.INIT != Instance.SessionStatus)
            {
                ARDebug.LogWarning("config when session is not init or paused, ignore it");
            }
            Instance.m_ndkSession.SessionAdapter.Config(config);
        }
    }
}
