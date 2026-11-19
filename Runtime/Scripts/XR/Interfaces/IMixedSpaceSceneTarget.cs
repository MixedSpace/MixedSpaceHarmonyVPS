using System;
using System.Threading.Tasks;
using UnityEngine;

namespace MixedSpace.XR
{
    /// <summary>
    /// Runtime output channel for one logical world. Localization publishes
    /// immutable alignment values here; the channel decides how Unity is updated.
    /// </summary>
    public interface IMixedSpaceWorldChannel : IDisposable
    {
        MixedSpaceWorldRoot View { get; }
        Matrix4x4 CurrentWorldPose { get; }

        Task PublishAsync(MixedSpaceAlignmentSolution solution);
        Matrix4x4 ProjectToLocal(Vector3 position, Quaternion rotation);
        Task ClearAsync();
        void Tick();
    }
}

