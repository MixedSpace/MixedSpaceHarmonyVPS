using System;
using UnityEngine;
using UnityEngine.Events;

namespace MixedSpace.XR
{
    /// <summary>
    /// Unity event view of session-owned map transitions. It contains no map
    /// history, frame processor or independently resettable state.
    /// </summary>
    [AddComponentMenu("MixedSpace/Views/Map Transition")]
    public sealed class MixedSpaceMapTransitionView : MonoBehaviour,
        IMixedSpaceLocalizationBinding
    {
        [SerializeField]
        private UnityEvent<int, int> m_MapTransition = new UnityEvent<int, int>();

        [SerializeField]
        private bool m_IncludeInitialVisit;

        public UnityEvent<int, int> MapTransition => m_MapTransition;

        public bool IncludeInitialVisit
        {
            get => m_IncludeInitialVisit;
            set => m_IncludeInitialVisit = value;
        }

        public IDisposable Connect(IMixedSpaceLocalizationReadModel source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.Subscribe(snapshot =>
            {
                if (!this || !snapshot.MapChanged ||
                    (snapshot.IsFirstFix && !m_IncludeInitialVisit))
                {
                    return;
                }

                m_MapTransition?.Invoke(snapshot.PreviousMapKey, snapshot.MapKey);
            });
        }
    }
}
