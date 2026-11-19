namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    ///     keeps the SDK's ARPose handles alive over the AREngineBridge
    /// </summary>
    /// <remarks>
    ///     the old native layer had an ARPose object per handle, which existed only
    ///     so a getter could write a pose into it and the caller could read it back
    ///     a line later. The bridge returns poses as return values instead, so
    ///     nothing routes through here on the hot path any more; the handle table
    ///     stays because ARPoseAdapter.Create is part of the adapter surface.
    ///     A slot is an index into m_Poses plus one, so IntPtr.Zero keeps meaning
    ///     "no pose".
    /// </remarks>
    internal class ARPoseAdapter
    {
        private readonly List<Pose> m_Poses = new List<Pose>();
        private readonly Stack<int> m_FreeSlots = new Stack<int>();

        public ARPoseAdapter(NDKSession session)
        {
        }

        public IntPtr Create()
        {
            return Create(Pose.identity);
        }

        public IntPtr Create(Pose pose)
        {
            // The caller's pose is already in Unity space, so it is stored as such;
            // GetPoseValue is the mirror image of this and hands the same value back.
            if (m_FreeSlots.Count > 0)
            {
                int slot = m_FreeSlots.Pop();
                m_Poses[slot] = pose;
                return new IntPtr(slot + 1);
            }
            m_Poses.Add(pose);
            return new IntPtr(m_Poses.Count);
        }

        public void Destroy(IntPtr poseHandle)
        {
            int slot = SlotOf(poseHandle);
            if (slot < 0 || m_FreeSlots.Contains(slot))
            {
                return;
            }
            m_Poses[slot] = Pose.identity;
            m_FreeSlots.Push(slot);
        }

        public Pose GetPoseValue(IntPtr poseHandle)
        {
            int slot = SlotOf(poseHandle);
            return slot < 0 ? Pose.identity : m_Poses[slot];
        }

        private int SlotOf(IntPtr poseHandle)
        {
            int slot = poseHandle.ToInt32() - 1;
            return slot < 0 || slot >= m_Poses.Count ? -1 : slot;
        }
    }
}
