using System;
using MixedSpace.XR;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MixedSpace
{
    /// <summary>
    /// Scene-local extension references, separate from reusable policy assets and
    /// retired embedded configuration. The runtime receives copies of the arrays.
    /// </summary>
    [Serializable]
    public sealed class MixedSpaceScenePorts
    {
        [SerializeField, MixedSpaceComponentContract(typeof(IMixedSpaceLocalizationBinding))]
        private Object[] m_LocalizationViews = Array.Empty<Object>();
        [SerializeField, MixedSpaceComponentContract(typeof(IMixedSpaceAlignmentSolver))]
        private Object m_AlignmentSolverOverride;
        [SerializeField, MixedSpaceComponentContract(typeof(IMixedSpaceLocalizationProvider))]
        private Object[] m_ExtensionProviders = Array.Empty<Object>();

        internal Object[] CopyViews() => (Object[])(m_LocalizationViews ?? Array.Empty<Object>()).Clone();
        internal Object[] CopyProviders() => (Object[])(m_ExtensionProviders ?? Array.Empty<Object>()).Clone();
        internal Object Solver => m_AlignmentSolverOverride;

        internal MixedSpaceScenePorts(Object[] views, Object solver, Object[] providers)
        {
            m_LocalizationViews = (Object[])(views ?? Array.Empty<Object>()).Clone();
            m_AlignmentSolverOverride = solver;
            m_ExtensionProviders = (Object[])(providers ?? Array.Empty<Object>()).Clone();
        }

        public MixedSpaceScenePorts() { }
    }
}
