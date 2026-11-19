using System;
using System.Collections.Generic;
using MixedSpace.XR;

namespace MixedSpace
{
    /// <summary>
    /// A session's latest solved fix, not a camera frame or a scene-write receipt.
    /// Revisions advance on both fixes and resets. An empty revision invalidates
    /// every presentation derived from the previous fix without resetting views
    /// through a second lifecycle/processor protocol.
    /// </summary>
    public sealed class MixedSpaceLocalizationSnapshot
    {
        internal MixedSpaceLocalizationSnapshot(long revision,
            MixedSpaceAlignmentRequest request = null,
            MixedSpaceAlignmentSolution solution = null, int previousMapKey = -1)
        {
            Revision = revision;
            Request = request;
            Solution = solution;
            PreviousMapKey = previousMapKey;
        }

        public long Revision { get; }
        public MixedSpaceAlignmentRequest Request { get; }
        public MixedSpaceAlignmentSolution Solution { get; }
        public int PreviousMapKey { get; }
        public bool HasFix => Request != null;
        public int MapKey => Request?.MapKey ?? -1;
        public bool IsFirstFix => HasFix && PreviousMapKey < 0;
        public bool MapChanged => HasFix && MapKey != PreviousMapKey;
    }

    /// <summary>
    /// Read-only application port. Subscriptions observe future revisions;
    /// Snapshot supplies the current value without replaying a historical event.
    /// Notifications run on the session/Unity thread and cannot transform poses.
    /// </summary>
    public interface IMixedSpaceLocalizationReadModel
    {
        MixedSpaceLocalizationSnapshot Snapshot { get; }
        IDisposable Subscribe(Action<MixedSpaceLocalizationSnapshot> changed);
    }

    /// <summary>
    /// A Unity presenter connects once, returning its teardown handle. It does not
    /// participate in localization execution, return a transformed frame, or own
    /// the session's map history. Implementations must not mutate solver state.
    /// </summary>
    public interface IMixedSpaceLocalizationBinding
    {
        IDisposable Connect(IMixedSpaceLocalizationReadModel source);
    }

    /// <summary>
    /// Single-writer state owned by one session. The writer API is internal;
    /// consumers only see immutable metadata through the read-only port.
    /// Subscriber failures are isolated from localization and other subscribers.
    /// This class is thread-confined, not a cross-thread event bus.
    /// </summary>
    public sealed class MixedSpaceLocalizationReadModel : IMixedSpaceLocalizationReadModel, IDisposable
    {
        private readonly List<Subscription> m_Subscriptions = new List<Subscription>();
        private readonly Action<Exception> m_ReportFailure;
        private bool m_Disposed;

        public MixedSpaceLocalizationReadModel(Action<Exception> reportFailure = null)
        {
            m_ReportFailure = reportFailure ?? (error =>
                MixedSpaceLogger.LogError($"Localization view failed: {error.Message}"));
        }

        public MixedSpaceLocalizationSnapshot Snapshot { get; private set; } =
            new MixedSpaceLocalizationSnapshot(0);

        public IDisposable Subscribe(Action<MixedSpaceLocalizationSnapshot> changed)
        {
            ThrowIfDisposed();
            if (changed == null) throw new ArgumentNullException(nameof(changed));
            var subscription = new Subscription(this, changed);
            m_Subscriptions.Add(subscription);
            return subscription;
        }

        internal void Record(MixedSpaceAlignmentRequest request, MixedSpaceAlignmentSolution solution)
        {
            ThrowIfDisposed();
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (solution.MapKey != request.MapKey)
                throw new ArgumentException("A solved fix must retain its source map identity.", nameof(solution));
            Publish(new MixedSpaceLocalizationSnapshot(
                Snapshot.Revision + 1, request, solution, Snapshot.MapKey));
        }

        internal void Clear()
        {
            ThrowIfDisposed();
            Publish(new MixedSpaceLocalizationSnapshot(Snapshot.Revision + 1));
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            // Invalidate pull-based presenters before detaching their listeners.
            Publish(new MixedSpaceLocalizationSnapshot(Snapshot.Revision + 1));
            foreach (Subscription subscription in m_Subscriptions.ToArray())
                subscription.Dispose();
        }

        private void Publish(MixedSpaceLocalizationSnapshot snapshot)
        {
            Snapshot = snapshot;
            foreach (Subscription subscription in m_Subscriptions.ToArray())
            {
                // A callback can reset/dispose the session reentrantly. Never
                // deliver an older fix after a newer empty/reset revision.
                if (!ReferenceEquals(Snapshot, snapshot)) break;
                try { subscription.Deliver(snapshot); }
                catch (Exception error)
                {
                    try { m_ReportFailure(error); }
                    catch { /* A diagnostic sink must not break localization. */ }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(MixedSpaceLocalizationReadModel));
        }

        private sealed class Subscription : IDisposable
        {
            private MixedSpaceLocalizationReadModel m_Owner;
            private Action<MixedSpaceLocalizationSnapshot> m_Changed;

            public Subscription(MixedSpaceLocalizationReadModel owner,
                Action<MixedSpaceLocalizationSnapshot> changed)
            { m_Owner = owner; m_Changed = changed; }

            public void Deliver(MixedSpaceLocalizationSnapshot snapshot) => m_Changed?.Invoke(snapshot);

            public void Dispose()
            {
                m_Owner?.m_Subscriptions.Remove(this);
                m_Owner = null;
                m_Changed = null;
            }
        }
    }

    /// <summary>
    /// Owns presentation connections for one runtime. Partial startup failure
    /// disconnects already attached views, and teardown attempts every handle.
    /// </summary>
    internal sealed class MixedSpaceLocalizationConnections : IDisposable
    {
        private readonly List<IDisposable> m_Handles = new List<IDisposable>();

        public MixedSpaceLocalizationConnections(IMixedSpaceLocalizationReadModel source,
            IEnumerable<IMixedSpaceLocalizationBinding> bindings)
        {
            try
            {
                foreach (IMixedSpaceLocalizationBinding binding in
                         bindings ?? Array.Empty<IMixedSpaceLocalizationBinding>())
                {
                    if (binding == null) continue;
                    m_Handles.Add(binding.Connect(source) ?? throw new InvalidOperationException(
                        "A localization binding must return a teardown handle."));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            IDisposable[] handles = m_Handles.ToArray();
            m_Handles.Clear();
            for (int index = handles.Length - 1; index >= 0; index--)
            {
                try { handles[index].Dispose(); }
                catch (Exception error)
                { MixedSpaceLogger.LogError($"Localization view disconnect failed: {error.Message}"); }
            }
        }
    }
}
