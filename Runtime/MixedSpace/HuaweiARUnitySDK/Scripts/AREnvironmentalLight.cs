namespace HuaweiARUnitySDK
{

    using UnityEngine;
    using HuaweiARInternal;

    /**
     * \if english
     * @brief A component that automatically adjust lighting settings for the scene 
     * to be inline with those estimated by AREngine.
     * 
     * Application cloud use a global parameter \c _GlobalLightEstimation in shader to render virtual objects.
     * \else
     * @brief 一个可以自动获取场景光照条件的Unity组件。
     * 
     * 应用可以shader的全局变量 \c _GlobalLightEstimation 来渲染虚拟物体。
     * \endif
     */
    [ExecuteInEditMode]
    public class AREnvironmentalLight : MonoBehaviour
    {
        void Update()
        {
#if UNITY_EDITOR
            Shader.SetGlobalFloat("_GlobalLightEstimation", 1.0f);
#else
            ARLightEstimate light = ARFrame.GetLightEstimate();
			if(!light.Valid){
				return;
			}
			const float middleGray = 0.466f;

			float normalizedIntensity = light.PixelIntensity / middleGray;
			Shader.SetGlobalFloat("_GlobalLightEstimation", normalizedIntensity);
#endif
        }
    }
}