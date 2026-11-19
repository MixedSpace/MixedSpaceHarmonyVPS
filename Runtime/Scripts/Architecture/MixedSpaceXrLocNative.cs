/* Native xrloc bindings for HarmonyOS and the Windows Editor. */

using System;
using System.Runtime.InteropServices;

namespace MixedSpace
{
    /// <summary>Native "Server" profile selector shown on the MixedSpaceSDK Inspector.</summary>
    public enum MixedSpaceXrLocServer
    {
        Production = 0,
        Test = 1
    }
}

namespace MixedSpace
{
    /// <summary>Status codes returned by every XrLoc entry point (XrLocStatus in xrloc_c.h).</summary>
    internal enum MixedSpaceXrLocStatus
    {
        Ok = 0,
        InvalidArgument = -1,
        Initialization = -2,
        LoadMap = -3,
        LocalizeFailed = -4,
        MapNotFound = -5,
        MapExists = -6,
        AuthorizationFailed = -7,
        Unknown = -99
    }

    /// <summary>Pixel layouts the library accepts for an input frame.</summary>
    internal enum MixedSpaceXrLocImageFormat
    {
        Gray = 0,
        Bgr = 1,
        Rgb = 2,
        Bgra = 3,
        Rgba = 4
    }

    /// <summary>Library version, read back as one 12-byte out parameter.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedSpaceXrLocVersion
    {
        public int major;
        public int minor;
        public int patch;
    }

    /// <summary>Pinhole intrinsics plus two radial and two tangential coefficients (64 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedSpaceXrLocCamera
    {
        public double fx;
        public double fy;
        public double cx;
        public double cy;
        public double k1;
        public double k2;
        public double p1;
        public double p2;
    }

    /// <summary>
    /// Raw image descriptor (32 bytes). stride 0 means rows are contiguous;
    /// rotationAngle is read whenever solver_type is not 1. The payload gates the
    /// angle source on solver_type alone and never looks at the prior pointer, so a
    /// caller that passes solver 0 with rotationAngle 0 pins the frame to an
    /// arbitrary quarter turn regardless of how good the prior is.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedSpaceXrLocImage
    {
        public IntPtr data;
        public int width;
        public int height;
        public int stride;
        public MixedSpaceXrLocImageFormat format;
        public int rotationAngle;
    }

    /// <summary>
    /// Localization output (112 bytes). rotation is a row-major 3x3 camera-to-world
    /// matrix and position is its translation column. success is one byte so the
    /// struct stays blittable and the trailing padding is never read as a flag.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedSpaceXrLocPose
    {
        public double r00;
        public double r01;
        public double r02;
        public double r10;
        public double r11;
        public double r12;
        public double r20;
        public double r21;
        public double r22;
        public double px;
        public double py;
        public double pz;
        public int confidence;
        public byte success;
        public int mapId;
    }

    /// <summary>One map point cloud sample (12 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedSpaceXrLocPosePrior
    {
        public int mapId;
        public double x;
        public double y;
        public double z;
    }

    /// <summary>
    /// 一个 12 字节的地图点云样本。
    /// One map point cloud sample (12 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MixedSpaceXrLocPoint3D
    {
        public float x;
        public float y;
        public float z;
    }

    /// <summary>
    /// Direct xrloc entries shared by authorization, map storage, localization and
    /// frame encoding. Both payloads export the same Cdecl symbols, so one constant
    /// serves the Windows Editor DLL and the Android ARM64 shared object.
    /// Every entry point is a thin extern: interpretation, marshalling of buffers and
    /// status mapping live in MixedSpaceXrLocCore.
    /// </summary>
    internal static class MixedSpaceXrLocNative
    {
        private const string Library = "xrloc";

        #region Authorization and configuration

        [DllImport(Library, EntryPoint = "XrLocSetInteger", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SetIntegerRaw([MarshalAs(UnmanagedType.LPStr)] string parameter, int value);

        [DllImport(Library, EntryPoint = "XrLocGetInteger", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetIntegerRaw([MarshalAs(UnmanagedType.LPStr)] string parameter, out int value);

        [DllImport(Library, EntryPoint = "XrLocVerifyToken", CallingConvention = CallingConvention.Cdecl)]
        private static extern int VerifyTokenRaw([MarshalAs(UnmanagedType.LPStr)] string token);

        [DllImport(Library, EntryPoint = "XrLocVerifyKey", CallingConvention = CallingConvention.Cdecl)]
        private static extern int VerifyKeyRaw([MarshalAs(UnmanagedType.LPStr)] string localKey);

        /// <summary>
        /// Selects the native server profile before token verification.
        /// Returns 0 (MixedSpaceXrLocStatus.Ok) on success; -1 means the key spelling
        /// or the value fell outside the accepted set (only 0 and 1 are valid).
        /// </summary>
        internal static int SetServer(int serverId)
        {
            return SetIntegerRaw("Server", serverId);
        }

        /// <summary>Returns 0 when the developer token is accepted by the native server profile.</summary>
        internal static int VerifyDeveloperToken(string developerToken)
        {
            return VerifyTokenRaw(developerToken);
        }

        /// <summary>
        /// Returns 0 when an offline activation key is accepted. This channel does not
        /// need a network round trip and is the only way to authorize a device that
        /// never reaches the licensing server.
        /// </summary>
        internal static int VerifyLocalKey(string localKey)
        {
            return VerifyKeyRaw(localKey);
        }

        /// <summary>
        /// Known integer knobs: "NumThreads" (-1 = system default) and
        /// "ImageCompressionLevel" (0-9, default 4). "Server" is handled by SetServer.
        /// </summary>
        internal static int SetInteger(string parameter, int value)
        {
            return SetIntegerRaw(parameter, value);
        }

        /// <summary>Reads one integer knob.</summary>
        internal static int GetInteger(string parameter, out int value)
        {
            return GetIntegerRaw(parameter, out value);
        }

        #endregion Authorization and configuration

        #region Diagnostics

        /// <summary>
        /// Installs the process-wide log sink. The callback signature is
        /// void (*)(const char*), identical to the PosePlugin sink, so the same
        /// managed thunk can serve both libraries.
        /// </summary>
        [DllImport(Library, EntryPoint = "XrLocRegisterLogCallback", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void RegisterLogCallback(IntPtr callback);

        [DllImport(Library, EntryPoint = "XrLocGetVersion", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetVersion(out MixedSpaceXrLocVersion version);

        #endregion Diagnostics

        #region Map storage

        /// <summary>
        /// Loads one map container. The caller owns the identifier, so the
        /// application map key can be reused directly; a repeated id returns
        /// MapExists instead of replacing the resident copy.
        /// </summary>
        [DllImport(Library, EntryPoint = "XrLocLoadMap", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadMap(int mapId, byte[] mapData, UIntPtr mapSize);

        [DllImport(Library, EntryPoint = "XrLocFreeMap", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FreeMap(int mapId);

        [DllImport(Library, EntryPoint = "XrLocPointsGetCount", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PointsGetCount(int mapId, out int count);

        /// <summary>Copies up to maxPoints samples into a caller-pinned buffer of MixedSpaceXrLocPoint3D.</summary>
        [DllImport(Library, EntryPoint = "XrLocPointsGet", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int PointsGet(int mapId, IntPtr points, int maxPoints, out int written);

        #endregion Map storage

        #region Localization

        /// <summary>
        /// Searches the explicit id array for the frame. Unlike the PosePlugin ABI
        /// there is no "all resident maps" sentinel, so callers must always pass the
        /// ids they loaded. rotationPriorXyzw may be null to let rotationAngle decide
        /// the image orientation; a non-null prior supersedes it.
        /// </summary>
        [DllImport(Library, EntryPoint = "XrLocLocalize", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Localize(
            int[] mapIds,
            int numMaps,
            ref MixedSpaceXrLocImage image,
            ref MixedSpaceXrLocCamera camera,
            int solverType,
            float[] rotationPriorXyzw,
            out MixedSpaceXrLocPose pose);

        [DllImport(Library, EntryPoint = "XrLocLocalizeWithPrior", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LocalizeWithPrior(
            int[] mapIds,
            int numMaps,
            ref MixedSpaceXrLocImage image,
            ref MixedSpaceXrLocCamera camera,
            int solverType,
            float[] rotationPriorXyzw,
            ref MixedSpaceXrLocPosePrior positionPrior,
            out MixedSpaceXrLocPose pose);

        #endregion Localization

        #region Lens correction

        /// <summary>
        /// Rectifies one fisheye frame with the Kannala-Brandt model (k1..k4). The
        /// export exists in xrloc 2.0.3 on both Windows and Android (verified with
        /// llvm-nm -D; an earlier comment here claimed it was removed — that was
        /// wrong). The export does NOT return output intrinsics: with alpha = 0 it
        /// keeps the original image area at the same resolution and the output
        /// pinhole matrix equals the input one, so callers use K' = K. Buffers are
        /// single-plane tightly packed rows with 1/3/4 channels; the output buffer
        /// must be at least width*height*channels bytes.
        /// </summary>
        [DllImport(Library, EntryPoint = "XrLocUndistortFishEye", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int UndistortFishEye(
            byte[] result,
            int resultSizeMax,
            byte[] pixels,
            int width,
            int height,
            int channels,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 4)] float[] intrinsics,
            float[] distortionCoefficients,
            float alpha,
            out int resultSize);

        #endregion Lens correction

        #region Frame encoding

        /// <summary>Encodes raw pixels into PNG. There is no matching-prep flag: the output is always a standalone PNG.</summary>
        [DllImport(Library, EntryPoint = "XrLocCaptureImage", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int CaptureImage(
            IntPtr capture,
            int captureSizeMax,
            IntPtr pixels,
            int width,
            int height,
            int channels,
            out int captureSize);

        #endregion Frame encoding
    }
}

