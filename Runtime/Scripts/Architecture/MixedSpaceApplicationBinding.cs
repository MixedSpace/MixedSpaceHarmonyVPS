using UnityEngine;
using UnityEngine.Events;

namespace MixedSpace
{
    /// <summary>
    /// Scene-owned application outputs. UI input is owned by application controllers
    /// that explicitly dispatch session commands, not by this event binding. Kept separate
    /// from reusable runtime policies; persistent UnityEvent targets stay in the scene.
    /// The SDK explicitly attaches the session, avoiding scene-wide SDK discovery.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MixedSpace/Application Binding")]
    public sealed class MixedSpaceApplicationBinding : MonoBehaviour
    {
        [SerializeField] private MixedSpaceApplicationSignals m_ApplicationSignals = new MixedSpaceApplicationSignals();
        [SerializeField, Tooltip("SDK 运行时初始化完成，可调用定位控制方法；不代表定位成功。\nSDK initialization completed; localization controls are available. This does not mean localization succeeded.")]
        private UnityEvent m_RuntimeReady = new UnityEvent();
        [SerializeField, Tooltip("Token 验证返回后触发，可读取 SDK 的 TokenValidationResult。\nRaised when token validation returns. Read TokenValidationResult on the SDK for the outcome.")]
        private UnityEvent m_EntitlementResolved = new UnityEvent();
        [SerializeField, Tooltip("运行时重建流程通知，不等于会话重置完成；就绪请监听 Runtime Ready。\nRuntime rebuild notification, not session reset completion. Listen to Runtime Ready for readiness.")]
        private UnityEvent m_RuntimeRebuilt = new UnityEvent();
        private MixedSpaceSDK m_Owner;

        /// <summary>定位、会话和跟踪事件集合，可在 Inspector 中绑定响应对象。</summary>
        public MixedSpaceApplicationSignals Signals => m_ApplicationSignals;
        /// <summary>运行时初始化完成事件，不代表定位成功。</summary>
        public UnityEvent Ready { get => m_RuntimeReady; set => m_RuntimeReady = value ?? new UnityEvent(); }
        /// <summary>Token 验证返回事件。</summary>
        public UnityEvent Entitlement { get => m_EntitlementResolved; set => m_EntitlementResolved = value ?? new UnityEvent(); }
        /// <summary>运行时重建流程通知；会话重置完成使用 LocalizationResetCompleted。</summary>
        public UnityEvent Rebuilt { get => m_RuntimeRebuilt; set => m_RuntimeRebuilt = value ?? new UnityEvent(); }

        internal void Attach(MixedSpaceSDK owner)
        {
            if (m_Owner != null && m_Owner != owner)
                throw new System.InvalidOperationException("An application binding cannot be shared by SDK instances.");
            m_Owner = owner;
        }

        internal void Detach()
        {
            m_Owner = null;
        }

        private void OnDestroy() { Detach(); }
    }
}
