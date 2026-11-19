using System;
using System.Net.Http;

namespace MixedSpace
{
    /// <summary>
    /// Provides token validation, connectivity, endpoints and authenticated request
    /// construction without coupling the SDK to one networking implementation.
    /// </summary>
    public interface IMixedSpaceNetworkEnvironment
    {
        bool IsNetworkAvailable { get; }
        string LocalizationServer { get; }
        string DownloadDirectory { get; }
        void ConfigureMobilePlatform(bool enabled);
        void ValidateToken(string developerToken, Action<bool> completion);
        HttpRequestMessage CreateUploadRequest(string endpoint);
    }

    /// <summary>
    /// Default MixedSpace network environment backed by the current token and
    /// endpoint provider. It is replaceable before SDK initialization.
    /// </summary>
    public sealed class MixedSpaceNetworkEnvironment : IMixedSpaceNetworkEnvironment
    {
        // Reachability is a connectivity hint, not proof of successful authorization.
        public bool IsNetworkAvailable => UnityEngine.Application.internetReachability != UnityEngine.NetworkReachability.NotReachable;
        public const string DefaultMapServer = "https://vps.njyjxr.com:18444/vpssdk/vps/proxy";
        public const string DefaultLocalizeServer = "https://vps.njyjxr.com:18444/vpssdk/vps/limitRatesVpslocalize";
        
        public const string DefaultTestMapServer = "http://192.168.1.170/vps/proxy";
        public const string DefaultTestLocalizeServer = "http://192.168.1.170/vps/limitRatesVpslocalize";
        
        private readonly Func<string> m_MapServer;
        private readonly Func<string> m_LocalizeServer;
        private readonly Func<int> m_XrLocServerId;
        public string LocalizationServer => ValidateServer(m_MapServer()).AbsoluteUri.TrimEnd('/');
        public string DownloadDirectory => "Map Data";

        /// <summary>
        /// Server sources are read lazily on every use so Inspector edits take
        /// effect without a domain reload. Null delegates fall back to defaults,
        /// which keeps parameterless construction working.
        /// </summary>
        public MixedSpaceNetworkEnvironment(
            Func<string> mapServer = null,
            Func<string> localizeServer = null,
            Func<int> xrLocServerId = null)
        {
            m_XrLocServerId = xrLocServerId ?? (() => (int)MixedSpaceXrLocServer.Production);
            m_MapServer = () => ResolveServer(mapServer?.Invoke(), m_XrLocServerId(), false);
            m_LocalizeServer = () => ResolveServer(localizeServer?.Invoke(), m_XrLocServerId(), true);
        }

        public static string ResolveServer(string configured, int serverId, bool localize)
        {
            string production = localize ? DefaultLocalizeServer : DefaultMapServer;
            string test = localize ? DefaultTestLocalizeServer : DefaultTestMapServer;
            if (serverId != (int)MixedSpaceXrLocServer.Production && serverId != (int)MixedSpaceXrLocServer.Test)
                throw new ArgumentOutOfRangeException(nameof(serverId), "Expected Production=0 or Test=1.");
            // Old scenes may store the other environment's default. Follow the
            // selector for built-in addresses while retaining custom endpoints.
            string value = configured?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(value) || value == production || value == test)
                return serverId == (int)MixedSpaceXrLocServer.Test ? test : production;
            return configured;
        }

        private static Uri ValidateServer(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Server must be an absolute HTTP(S) URL without credentials or fragments.");
            return uri;
        }

        public void ConfigureMobilePlatform(bool enabled)
        {
            // Platform configuration hook for custom network environments; the default implementation is stateless.
        }

        public void ValidateToken(string developerToken, Action<bool> completion)
        {
            if (completion == null)
                throw new ArgumentNullException(nameof(completion));

            // Delegate authorization to the native verification API, carrying the
            // Inspector-selected xrloc server profile (read fresh per call).
            completion(MixedSpaceNativeAuthorization.Validate(developerToken, m_XrLocServerId()));
        }

        public HttpRequestMessage CreateUploadRequest(string endpoint)
        {
            MixedSpaceUploadRoute.ValidateEndpoint(endpoint);

            // Only localization uses the dedicated gateway. Other upload endpoints
            // belong to the same proxy namespace as typed POST operations.
            if (endpoint != "localize")
                return new HttpRequestMessage(HttpMethod.Post,
                    MixedSpaceUploadRoute.ProxyEndpoint(LocalizationServer, endpoint));

            // Full gateway URL; never append another /localize segment on iOS or Android.
            return new HttpRequestMessage(HttpMethod.Post, ValidateServer(m_LocalizeServer()));
        }
    }

    /// <summary>Pure request routing; never logs credentials or sends a request.</summary>
    internal static class MixedSpaceUploadRoute
    {
        internal static void ValidateEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Upload endpoint is required.");
            foreach (char c in endpoint)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '_' && c != '-')
                    throw new ArgumentException("Upload endpoint must be a single route segment.");
        }

        internal static Uri ProxyEndpoint(string server, string endpoint)
        {
            ValidateEndpoint(endpoint);
            var uri = new Uri(server, UriKind.Absolute);
            var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" + endpoint };
            return builder.Uri;
        }

        // Normalize a duplicated gateway route segment once. Preserve query parameters,
        // normal /proxy/localize routes and already-correct gateway URLs.
        internal static Uri DropAppendedUploadSegment(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri) throw new ArgumentException("Absolute upload URI is required.");
            string path = uri.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith("/limitRatesVpslocalize/localize", StringComparison.Ordinal)) return uri;
            return new UriBuilder(uri) { Path = path.Substring(0, path.Length - "/localize".Length) }.Uri;
        }
    }
}
