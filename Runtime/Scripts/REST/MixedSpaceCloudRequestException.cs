using System;

namespace MixedSpace.REST
{
    public enum MixedSpaceCloudFailureKind { Http, Transport, Timeout, Decode }

    /// <summary>
    /// Safe cloud failure metadata. No raw URI, response body, headers or inner
    /// exception is retained: any of those can contain application credentials.
    /// HTTP 200 with success=false remains a normal localization miss, not this exception.
    /// </summary>
    public sealed class MixedSpaceCloudRequestException : Exception
    {
        public MixedSpaceCloudFailureKind Kind { get; }
        public int? StatusCode { get; }
        public string Operation { get; }
        internal MixedSpaceCloudRequestException(MixedSpaceCloudFailureKind kind, string operation, int? status = null)
            : base("Cloud " + operation + " failed: " + kind + (status.HasValue ? " HTTP " + status.Value : "") + ".")
        { Kind = kind; Operation = operation; StatusCode = status; }
    }
}
