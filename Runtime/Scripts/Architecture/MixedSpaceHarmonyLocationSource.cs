#if UNITY_OPENHARMONY && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine.OpenHarmony;

namespace MixedSpace
{
    // Public operations use the Unity thread, matching the mobile source contract.
    // Native callbacks only copy value data and never touch Unity objects.
    internal sealed class MixedSpaceHarmonyLocationSource : IMixedSpaceLocationSource
    {
        private static readonly object RoutesGate = new object();
        private static readonly Dictionary<IntPtr, MixedSpaceHarmonyLocationSource> Routes =
            new Dictionary<IntPtr, MixedSpaceHarmonyLocationSource>();
        private static readonly object CleanupGate = new object();
        private static readonly List<IntPtr> PendingCleanup = new List<IntPtr>();
        private static readonly Native.LocationCallback Callback = Receive;
        private static long s_NextRoute;
        private readonly object m_Transition = new object();
        private readonly object m_State = new object();
        private IntPtr m_Config, m_Route;
        private bool m_Requested, m_Disposed, m_HasFix, m_PermissionPending;
        private MixedSpaceLocationSample m_Sample;
        private PermissionCallbacks m_PermissionCallbacks;
        private int m_ReportedUnavailable;

        public bool IsAvailable => Probe(out _);
        public bool IsServiceEnabled => Probe(out bool enabled) && enabled;

        private bool Probe(out bool enabled)
        {
            enabled = false;
            try { return Native.IsEnabled(out enabled) == 0; }
            catch (DllNotFoundException error) { ReportUnavailable(error); return false; }
            catch (EntryPointNotFoundException error) { ReportUnavailable(error); return false; }
        }

        private void ReportUnavailable(Exception error)
        {
            if (Interlocked.Exchange(ref m_ReportedUnavailable, 1) == 0)
                MixedSpaceLogger.LogWarning("Harmony location service unavailable (" + error.GetType().Name + ").");
        }

        public bool TryStart()
        {
            lock (m_Transition)
            {
                lock (m_State) { if (m_Disposed) return false; }
                if (!IsServiceEnabled) return false;
                if (m_Config != IntPtr.Zero) return true;
                lock (m_State) m_Requested = true;
                if (Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
                    return StartNative();
                if (m_PermissionPending) return true;
                m_PermissionPending = true;
                m_PermissionCallbacks = new PermissionCallbacks();
                m_PermissionCallbacks.PermissionGranted += OnPermissionGranted;
                m_PermissionCallbacks.PermissionDenied += OnPermissionDenied;
                Permission.RequestUserPermissions(new[] { Permission.CoarseLocation, Permission.FineLocation },
                    m_PermissionCallbacks);
                return true;
            }
        }

        private void OnPermissionGranted(string permission)
        {
            lock (m_Transition)
            {
                if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation)) return;
                m_PermissionPending = false;
                lock (m_State) { if (m_Disposed || !m_Requested) return; }
                StartNative();
            }
        }

        private void OnPermissionDenied(string permission)
        {
            if (permission != Permission.CoarseLocation) return;
            lock (m_Transition)
            {
                m_PermissionPending = false;
                lock (m_State) { m_Requested = false; m_HasFix = false; }
                MixedSpaceLogger.LogWarning("Harmony location permission was denied.");
            }
        }

        private bool StartNative()
        {
            // m_Transition is held. A synchronous native callback may take m_State.
            if (m_Config != IntPtr.Zero) return true;
            RetryCleanup();
            IntPtr config = Native.CreateConfig();
            if (config == IntPtr.Zero) return FailStart(-1);
            IntPtr route = new IntPtr(Interlocked.Increment(ref s_NextRoute));
            Native.SetUseScene(config, 0x0401); // LOCATION_USE_SCENE_NAVIGATION
            Native.SetInterval(config, 1);
            Native.SetCallback(config, Callback, route);
            lock (RoutesGate) Routes[route] = this;
            int status = Native.Start(config);
            if (status != 0)
            {
                lock (RoutesGate) Routes.Remove(route);
                Native.DestroyConfig(config);
                return FailStart(status);
            }
            m_Config = config;
            m_Route = route;
            return true;
        }

        private bool FailStart(int status)
        {
            lock (m_State) { m_Requested = false; m_HasFix = false; }
            MixedSpaceLogger.LogWarning("Harmony location startup failed: status=" + status + ".");
            return false;
        }

        public bool TryRead(out MixedSpaceLocationSample sample)
        {
            sample = default;
            if (!IsServiceEnabled || !Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
            {
                lock (m_State) m_HasFix = false;
                return false;
            }
            lock (m_State)
            {
                if (m_Disposed || !m_Requested || !m_HasFix) return false;
                sample = m_Sample;
                return true;
            }
        }

        public void Stop()
        {
            lock (m_Transition)
            {
                lock (m_State) { m_Requested = false; m_HasFix = false; }
                IntPtr config = m_Config;
                m_Config = IntPtr.Zero;
                lock (RoutesGate) Routes.Remove(m_Route);
                m_Route = IntPtr.Zero;
                if (config == IntPtr.Zero) return;
                int status = Native.Stop(config);
                if (status == 0) Native.DestroyConfig(config);
                else
                {
                    // Permission revocation may reject Stop. Keep native configuration alive
                    // until unsubscribe succeeds; late callbacks have no managed route.
                    lock (CleanupGate) PendingCleanup.Add(config);
                    MixedSpaceLogger.LogWarning("Harmony location cleanup deferred: status=" + status + ".");
                }
            }
        }

        private static void RetryCleanup()
        {
            lock (CleanupGate)
            {
                for (int i = PendingCleanup.Count - 1; i >= 0; --i)
                {
                    IntPtr config = PendingCleanup[i];
                    if (Native.Stop(config) != 0) continue;
                    Native.DestroyConfig(config);
                    PendingCleanup.RemoveAt(i);
                }
            }
        }

        public void Dispose()
        {
            lock (m_Transition)
            {
                lock (m_State) m_Disposed = true;
                Stop();
            }
        }

        [AOT.MonoPInvokeCallback(typeof(Native.LocationCallback))]
        private static void Receive(IntPtr location, IntPtr route)
        {
            if (location == IntPtr.Zero) return;
            try
            {
                MixedSpaceHarmonyLocationSource owner;
                lock (RoutesGate) { if (!Routes.TryGetValue(route, out owner)) return; }
                Native.BasicInfo info = Native.GetBasicInfo(location);
                if (info.TimeForFix <= 0 || !Finite(info.Latitude) || !Finite(info.Longitude) ||
                    !Finite(info.Altitude) || !Finite(info.Accuracy) || !Finite(info.AltitudeAccuracy) ||
                    info.Latitude < -90 || info.Latitude > 90 || info.Longitude < -180 || info.Longitude > 180 ||
                    info.Accuracy < 0) return;
                lock (owner.m_State)
                {
                    if (owner.m_Disposed || !owner.m_Requested) return;
                    owner.m_Sample = new MixedSpaceLocationSample(info.Latitude, info.Longitude,
                        info.Altitude, info.Accuracy, info.AltitudeAccuracy);
                    owner.m_HasFix = true;
                }
            }
            catch { /* Managed exceptions must never escape a native callback. */ }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static class Native
        {
            private const string Library = "liblocation_ndk.so";
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            internal delegate void LocationCallback(IntPtr location, IntPtr userData);
            [StructLayout(LayoutKind.Sequential)]
            internal struct BasicInfo
            {
                public double Latitude, Longitude, Altitude, Accuracy, Speed, Direction;
                public long TimeForFix, TimeSinceBoot;
                public double AltitudeAccuracy, SpeedAccuracy, DirectionAccuracy;
                public long UncertaintyOfTimeSinceBoot;
                public int LocationSourceType;
            }
            [DllImport(Library, EntryPoint = "OH_Location_IsLocatingEnabled", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int IsEnabled([MarshalAs(UnmanagedType.I1)] out bool enabled);
            [DllImport(Library, EntryPoint = "OH_Location_CreateRequestConfig", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr CreateConfig();
            [DllImport(Library, EntryPoint = "OH_Location_DestroyRequestConfig", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void DestroyConfig(IntPtr config);
            [DllImport(Library, EntryPoint = "OH_LocationRequestConfig_SetUseScene", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void SetUseScene(IntPtr config, int scene);
            [DllImport(Library, EntryPoint = "OH_LocationRequestConfig_SetInterval", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void SetInterval(IntPtr config, int interval);
            [DllImport(Library, EntryPoint = "OH_LocationRequestConfig_SetCallback", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void SetCallback(IntPtr config, LocationCallback callback, IntPtr userData);
            [DllImport(Library, EntryPoint = "OH_Location_StartLocating", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Start(IntPtr config);
            [DllImport(Library, EntryPoint = "OH_Location_StopLocating", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Stop(IntPtr config);
            [DllImport(Library, EntryPoint = "OH_LocationInfo_GetBasicInfo", CallingConvention = CallingConvention.Cdecl)]
            internal static extern BasicInfo GetBasicInfo(IntPtr location);
        }
    }
}
#endif
