// Managed stand-ins for the native *List objects the old AREngine plugin created
// with HwArTrackableList_create and friends.
//
// Those lists were only ever a way to move a batch of handles from native code
// into C#: AREngine filled the list, C# read its size, acquired each item and
// destroyed the list. The bridge returns batches as records instead, so the
// adapters now build the item list themselves and this file is what the old
// Create/GetSize/AcquireItem/Destroy signatures keep working against.

namespace HuaweiARInternal
{
    using System;
    using System.Collections.Generic;

    internal sealed class BridgeHandleList
    {
        /// <summary>the handles currently in the list, in insertion order</summary>
        public readonly List<IntPtr> Handles = new List<IntPtr>();
    }

    /// <summary>
    ///     hands out IntPtr tokens for managed handle lists
    /// </summary>
    /// <remarks>
    ///     a token is the slot index plus one, because the adapter surface treats
    ///     IntPtr.Zero as "no list" and callers compare against it
    /// </remarks>
    internal static class BridgeHandleLists
    {
        private static readonly List<BridgeHandleList> m_Slots = new List<BridgeHandleList>();
        private static readonly Stack<int> m_FreeSlots = new Stack<int>();

        public static IntPtr Create()
        {
            if (m_FreeSlots.Count > 0)
            {
                int slot = m_FreeSlots.Pop();
                m_Slots[slot] = new BridgeHandleList();
                return new IntPtr(slot + 1);
            }
            m_Slots.Add(new BridgeHandleList());
            return new IntPtr(m_Slots.Count);
        }

        public static void Destroy(IntPtr listHandle)
        {
            int slot = SlotOf(listHandle);
            if (slot < 0 || m_Slots[slot] == null)
            {
                return;
            }
            m_Slots[slot] = null;
            m_FreeSlots.Push(slot);
        }

        public static int Size(IntPtr listHandle)
        {
            BridgeHandleList list = Get(listHandle);
            return list == null ? 0 : list.Handles.Count;
        }

        public static IntPtr ItemAt(IntPtr listHandle, int index)
        {
            BridgeHandleList list = Get(listHandle);
            if (list == null || index < 0 || index >= list.Handles.Count)
            {
                return IntPtr.Zero;
            }
            return list.Handles[index];
        }

        public static void Add(IntPtr listHandle, IntPtr handle)
        {
            BridgeHandleList list = Get(listHandle);
            if (list != null)
            {
                list.Handles.Add(handle);
            }
        }

        public static void Clear(IntPtr listHandle)
        {
            BridgeHandleList list = Get(listHandle);
            if (list != null)
            {
                list.Handles.Clear();
            }
        }

        private static BridgeHandleList Get(IntPtr listHandle)
        {
            int slot = SlotOf(listHandle);
            return slot < 0 ? null : m_Slots[slot];
        }

        private static int SlotOf(IntPtr listHandle)
        {
            int slot = listHandle.ToInt32() - 1;
            return slot < 0 || slot >= m_Slots.Count ? -1 : slot;
        }
    }

    /// <summary>
    ///     hands out IntPtr tokens for the managed records that stand in for the
    ///     native objects the old layer allocated per hit result or per point cloud
    /// </summary>
    /// <remarks>
    ///     the old HwArHitResult_create/HwArPointCloud_acquire returned a pointer the
    ///     caller could keep reading until it destroyed the object, and the SDK's
    ///     façades rely on that: ARHitResult reads its distance lazily, and
    ///     ARPointCloud refuses to read once Release() has zeroed its handle. A slot
    ///     table keeps both behaviours, and releasing a token makes the slot free for
    ///     the next result rather than leaving a dangling pointer behind.
    /// </remarks>
    internal sealed class BridgeTokenTable<T>
    {
        private readonly List<T> m_Slots = new List<T>();
        private readonly List<bool> m_Live = new List<bool>();
        private readonly Stack<int> m_FreeSlots = new Stack<int>();

        /// <summary>
        ///     takes a slot, fills it and returns its token
        /// </summary>
        public IntPtr Store(T value)
        {
            if (m_FreeSlots.Count > 0)
            {
                int slot = m_FreeSlots.Pop();
                m_Slots[slot] = value;
                m_Live[slot] = true;
                return new IntPtr(slot + 1);
            }
            m_Slots.Add(value);
            m_Live.Add(true);
            return new IntPtr(m_Slots.Count);
        }

        /// <summary>
        ///     reads the record behind a token
        /// </summary>
        /// <returns>false once the token has been released, or was never one</returns>
        public bool TryGet(IntPtr token, out T value)
        {
            int slot = SlotOf(token);
            if (slot < 0 || !m_Live[slot])
            {
                value = default(T);
                return false;
            }
            value = m_Slots[slot];
            return true;
        }

        /// <summary>
        ///     returns a token's slot to the table; releasing twice is harmless
        /// </summary>
        public void Release(IntPtr token)
        {
            int slot = SlotOf(token);
            if (slot < 0 || !m_Live[slot])
            {
                return;
            }
            m_Live[slot] = false;
            m_Slots[slot] = default(T);
            m_FreeSlots.Push(slot);
        }

        private int SlotOf(IntPtr token)
        {
            int slot = token.ToInt32() - 1;
            return slot < 0 || slot >= m_Slots.Count ? -1 : slot;
        }
    }
}
