namespace HuaweiARInternal
{
    using System;

    internal class NDKSession
    {

        public IntPtr SessionHandle { get; private set; }
        public IntPtr FrameHandle { get; set; }
        public IntPtr ConfigHandle { get; set; }
        public ARAnchorManager AnchorManager { get; private set; }
        public ARTrackableManager TrackableManager { get; private set; }
        public ARHitResultAdapter HitResultAdapter { get; private set; }
        public ARPointCloudAdapter PointCloudAdapter { get; private set; }
        public ARFrameAdapter FrameAdapter { get; private set; }
        public ARSessionAdapter SessionAdapter { get; private set; }
        public ARCameraAdapter CameraAdapter { get; private set; }
        public ARPoseAdapter PoseAdapter { get; private set; }
        public ARConfigBaseAdapter ConfigBaseAdapter { get; private set; }
        public ARImageAdapter ImageAdapter { get; private set; }
     
        public ARAnchorAdapter AnchorAdapter { get; private set; }


        public ARTrackbaleAdapter TrackableAdapter { get; private set; }
        public ARPlaneAdapter PlaneAdapter { get; private set; }
        public ARPointAdapter PointAdapter { get; private set; }    

        //ARCameraConfig
        public ARCameraConfigAdapter CameraConfigAdapter { get; private set; }

        public ARAugmentedImageAdapter AugmentedImageAdapter { get; private set; }
        public ARAugmentedImageDatabaseAdapter AugmentedImageDatabaseAdapter { get; private set; }
        public ARSceneMeshAdapter SceneMeshAdapter { get; private set; }
        public ARSemanticDenseAdapter SemanticDenseAdapter { get; private set; }
        public ARFaceGeometryAdapter FaceGeometryAdapter { get; private set; }
        public ARFaceAdapter FaceAdapter { get; private set; }
        public ARFaceBlendShapeAdapter FaceBlendShapeAdapter {  get; private set; }

        public NDKSession()
        {
            AnchorAdapter = new ARAnchorAdapter(this);
            HitResultAdapter = new ARHitResultAdapter(this);
            PointCloudAdapter = new ARPointCloudAdapter(this);
            AnchorManager = new ARAnchorManager(this);
            TrackableManager = new ARTrackableManager(this);
            FrameAdapter = new ARFrameAdapter(this);
            SessionAdapter = new ARSessionAdapter(this);
            CameraAdapter = new ARCameraAdapter(this);
            PoseAdapter = new ARPoseAdapter(this);
            ConfigBaseAdapter = new ARConfigBaseAdapter(this);
            ImageAdapter = new ARImageAdapter(this);
            
            TrackableAdapter = new ARTrackbaleAdapter(this);
            PlaneAdapter = new ARPlaneAdapter(this);
            PointAdapter = new ARPointAdapter(this);
            AugmentedImageAdapter = new ARAugmentedImageAdapter(this);
            AugmentedImageDatabaseAdapter = new ARAugmentedImageDatabaseAdapter(this);
            SceneMeshAdapter = new ARSceneMeshAdapter(this);
            SemanticDenseAdapter = new ARSemanticDenseAdapter(this);
            FaceGeometryAdapter = new ARFaceGeometryAdapter(this);
            FaceAdapter = new ARFaceAdapter(this);
            FaceBlendShapeAdapter = new ARFaceBlendShapeAdapter(this);

            //ARCameraConfig
            CameraConfigAdapter = new ARCameraConfigAdapter(this);
     

            SessionHandle = SessionAdapter.Create();
        }
        public void CreateFrame()
        {
            FrameHandle = FrameAdapter.Create();
        }

        private bool m_disposed = false;

        // 显式销毁 native 会话资源。正常生命周期(退出/重建)应走这里，而不是依赖 GC Finalizer：
        // 销毁后置零所有 handle 并 SuppressFinalize，杜绝重复 destroy 与悬空 handle 被再次使用。
        public void DestroyNative()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            if (FrameHandle != IntPtr.Zero)
            {
                FrameAdapter.Destroy(FrameHandle);
                FrameHandle = IntPtr.Zero;
            }
            if (SessionHandle != IntPtr.Zero)
            {
                SessionAdapter.Destroy();
                SessionHandle = IntPtr.Zero;
            }
            if (ConfigHandle != IntPtr.Zero)
            {
                ConfigBaseAdapter.Destroy(ConfigHandle);
                ConfigHandle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~NDKSession()
        {
            // 兜底清理：正常运行期 NDKSession 由 ARSessionManager.m_ndkSession 强引用
            // (见 CreateSession 幂等保护)，不应被 GC 回收、更不应在 AR 运行期间走到这里。
            // 若已显式 DestroyNative 过则跳过，避免重复销毁 native 会话。
            if (m_disposed)
            {
                return;
            }
            if (FrameHandle != IntPtr.Zero)
            {
                FrameAdapter.Destroy(FrameHandle);
            }
            if(SessionHandle != IntPtr.Zero)
            {
                SessionAdapter.Destroy();
            }
            if(ConfigHandle != IntPtr.Zero)
            {
                ConfigBaseAdapter.Destroy(ConfigHandle);
            }
        }
    }
}
