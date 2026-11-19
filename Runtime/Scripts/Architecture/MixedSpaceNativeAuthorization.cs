namespace MixedSpace
{
    /// <summary>
    /// Pure authorization orchestration: picks the platform's native entry and
    /// interprets status codes. No DllImport here — xrloc bindings live in
    /// MixedSpaceXrLocNative.
    /// </summary>
    internal static class MixedSpaceNativeAuthorization
    {
        /// <summary>
        /// Returns true only for native status zero; loading failures propagate to runtime diagnostics.
        /// The xrloc server profile is re-asserted from the Inspector selection before verifying:
        /// the Editor cloud binding path can run before SDK initialization, so startup-time
        /// configuration alone cannot cover it.
        /// </summary>
        internal static bool Validate(string developerToken, int xrLocServerId)
        {
            if (string.IsNullOrWhiteSpace(developerToken))
            {
                MixedSpaceLogger.LogError("[TOKEN_MISSING] Token 验证失败：未配置 Developer Token。");
                return false;
            }
            int serverStatus = MixedSpaceXrLocNative.SetServer(xrLocServerId);
            if (serverStatus != 0)
                MixedSpaceLogger.LogWarning($"XrLoc server selection rejected: status={serverStatus}");
            int status = MixedSpaceXrLocNative.VerifyDeveloperToken(developerToken);
            if (status == 0) return true;
            MixedSpaceLogger.LogError($"[TOKEN_VALIDATION_FAILED] Token 验证失败：原生返回 status={status}，请检查 Token、网络连接和服务器环境。");
            return false;
        }
    }
}
