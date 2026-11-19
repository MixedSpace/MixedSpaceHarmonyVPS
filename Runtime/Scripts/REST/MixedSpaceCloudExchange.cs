using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MixedSpace.REST
{
    internal enum MixedSpaceCloudOperation { Post, Download, Upload }

    /// <summary>
    /// Diagnostics accept only safe metadata. URLs, headers, request/response
    /// bodies, reason phrases and exception messages cannot cross this boundary.
    /// </summary>
    internal interface IMixedSpaceCloudDiagnostics
    {
        void HttpFailure(MixedSpaceCloudOperation operation, int status);
        void TransportFailure(MixedSpaceCloudOperation operation, Type exceptionType);
    }

    internal sealed class MixedSpaceCloudDiagnostics : IMixedSpaceCloudDiagnostics
    {
        public void HttpFailure(MixedSpaceCloudOperation operation, int status) =>
            MixedSpaceLogger.LogWarning("Cloud " + operation + " returned HTTP " + status + ".");
        public void TransportFailure(MixedSpaceCloudOperation operation, Type exceptionType) =>
            MixedSpaceLogger.LogError("Cloud " + operation + " failed (" + exceptionType.Name + ").");
    }

    /// <summary>
    /// One response transaction for all cloud operations. Own the request,
    /// response and buffer until decoding finishes. The result contains no live
    /// HTTP resource. HTTP/transport/decode failures are typed exceptions, never default DTOs.
    /// </summary>
    internal sealed class MixedSpaceCloudExchange
    {
        private readonly IMixedSpaceHttpTransport m_Transport;
        private readonly IMixedSpaceCloudDiagnostics m_Diagnostics;
        public MixedSpaceCloudExchange(IMixedSpaceHttpTransport transport, IMixedSpaceCloudDiagnostics diagnostics)
        {
            m_Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public async Task<T> ExecuteAsync<T>(HttpRequestMessage request, Func<byte[], T> decode,
            IProgress<float> progress, CancellationToken cancellationToken, MixedSpaceCloudOperation operation)
        {
            using (request)
            using (var destination = new MemoryStream())
            {
                bool decoding = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (HttpResponseMessage response = await m_Transport.DownloadAsync(
                        request, destination, progress, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (response == null) throw new InvalidOperationException("Transport returned no response.");
                        if (!response.IsSuccessStatusCode)
                        {
                            try { m_Diagnostics.HttpFailure(operation, (int)response.StatusCode); }
                            catch { /* Diagnostics must not replace the original failure. */ }
                            throw new MixedSpaceCloudRequestException(MixedSpaceCloudFailureKind.Http, operation.ToString(), (int)response.StatusCode);
                        }
                        decoding = true;
                        T value = decode(destination.ToArray());
                        if (value is null) throw new InvalidDataException("Cloud response decoded to null.");
                        cancellationToken.ThrowIfCancellationRequested();
                        return value;
                    }
                }
                catch (MixedSpaceCloudRequestException) { throw; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    try { m_Diagnostics.TransportFailure(operation, exception.GetType()); }
                    catch { /* Do not replace an operation result with a logging failure. */ }
                    throw new MixedSpaceCloudRequestException(decoding ? MixedSpaceCloudFailureKind.Decode :
                        exception is OperationCanceledException ? MixedSpaceCloudFailureKind.Timeout : MixedSpaceCloudFailureKind.Transport,
                        operation.ToString());
                }
            }
        }
    }
}
