namespace HuaweiARUnitySDK
{
    using HuaweiARInternal;
    using System;
    using UnityEngine;

    /**
     * \if english
     * @brief A configuration used to track a face.
     * \else
     * @brief 用于跟踪人脸的一个配置项。
     * \endif
     */
    [CreateAssetMenu(fileName = "HuaweiARConfig", menuName = "HuaweiARUnitySDK/FaceTrackingConfig", order = 3)]
    public class ARFaceTrackingConfig : ARConfigBase
    {
        private NDKARType arType = NDKARType.FACE_AR;

        /**
         * \if english
         * Focus mode of this configuratioin. Default is AUTO_FOCUS.
         * \else
         * 对焦模式，默认是自动对焦。
         * \endif
         */
        public ARConfigFocusMode FocusMode = ARConfigFocusMode.AUTO_FOCUS;
        internal override int GetARType() { return (int)arType; }

        internal override ARConfigFocusMode GetFocusMode()
        {
            return FocusMode;
        }

        internal override void SetFocusMode(ARConfigFocusMode focusMode)
        {
            FocusMode = focusMode;
        }
    }
}
///@endcond