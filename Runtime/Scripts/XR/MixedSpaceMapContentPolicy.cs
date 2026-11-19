using System;

namespace MixedSpace.XR
{
    /// <summary>
    /// Describes where a map's native binary comes from. ProviderManaged is used
    /// by cloud/geo providers that do not load a local native map at all.
    /// </summary>
    public enum MixedSpaceMapContentLocation
    {
        ProviderManaged = 0,
        EmbeddedAsset = 1,
        RemoteBinary = 2,
        InMemory = 3
    }

    /// <summary>
    /// Immutable data policy for one map. This replaces the former open-ended
    /// IMapOption bag so repository behavior is explicit and type-safe.
    /// </summary>
    public sealed class MixedSpaceMapContentPolicy
    {
        private readonly byte[] m_RuntimePayload;

        public MixedSpaceMapContentPolicy(
            MixedSpaceMapContentLocation location,
            bool presentPreviewAtRuntime = false,
            byte[] runtimePayload = null)
        {
            if (location == MixedSpaceMapContentLocation.InMemory &&
                (runtimePayload == null || runtimePayload.Length == 0))
            {
                throw new ArgumentException(
                    "In-memory map content requires a non-empty payload.",
                    nameof(runtimePayload));
            }
            if (location != MixedSpaceMapContentLocation.InMemory &&
                runtimePayload is { Length: > 0 })
            {
                throw new ArgumentException(
                    "A runtime payload is only valid for in-memory map content.",
                    nameof(runtimePayload));
            }

            Location = location;
            PresentPreviewAtRuntime = presentPreviewAtRuntime;
            m_RuntimePayload = runtimePayload == null
                ? null
                : (byte[])runtimePayload.Clone();
        }

        public MixedSpaceMapContentLocation Location { get; }
        public bool PresentPreviewAtRuntime { get; }

        internal byte[] CopyRuntimePayload()
        {
            return m_RuntimePayload == null
                ? null
                : (byte[])m_RuntimePayload.Clone();
        }

        public static MixedSpaceMapContentPolicy ProviderManaged(
            bool presentPreviewAtRuntime = false)
        {
            return new MixedSpaceMapContentPolicy(
                MixedSpaceMapContentLocation.ProviderManaged,
                presentPreviewAtRuntime);
        }

        public static MixedSpaceMapContentPolicy Embedded(
            bool presentPreviewAtRuntime = false)
        {
            return new MixedSpaceMapContentPolicy(
                MixedSpaceMapContentLocation.EmbeddedAsset,
                presentPreviewAtRuntime);
        }

        public static MixedSpaceMapContentPolicy Remote(
            bool presentPreviewAtRuntime = false)
        {
            return new MixedSpaceMapContentPolicy(
                MixedSpaceMapContentLocation.RemoteBinary,
                presentPreviewAtRuntime);
        }

        public static MixedSpaceMapContentPolicy FromMemory(
            byte[] payload,
            bool presentPreviewAtRuntime = false)
        {
            return new MixedSpaceMapContentPolicy(
                MixedSpaceMapContentLocation.InMemory,
                presentPreviewAtRuntime,
                payload);
        }
    }
}
