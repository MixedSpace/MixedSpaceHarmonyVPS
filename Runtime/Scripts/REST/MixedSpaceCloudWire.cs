using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MixedSpace.REST
{
    internal interface IMixedSpaceCloudCodec
    {
        string Encode<T>(T value);
        T Decode<T>(byte[] bytes);
    }

    /// <summary>
    /// The deployed service uses Unity JSON and ASCII response/upload metadata.
    /// Preserve that wire contract in this refactor; changing encodings requires
    /// a separate protocol migration. POST StringContent retains its UTF-8 default.
    /// </summary>
    internal sealed class MixedSpaceUnityCloudCodec : IMixedSpaceCloudCodec
    {
        public string Encode<T>(T value) => JsonUtility.ToJson(value);
        public T Decode<T>(byte[] bytes) => JsonUtility.FromJson<T>(Encoding.ASCII.GetString(bytes));
    }

    /// <summary>
    /// Builds an owned HTTP request without sending it. Errors in authoring or
    /// serialization propagate to callers instead of becoming network misses.
    /// Authentication headers supplied by the environment are left unchanged.
    /// </summary>
    internal sealed class MixedSpaceCloudRequestFactory
    {
        internal const string UrlFormat = "{0}/{1}";
        private readonly IMixedSpaceNetworkEnvironment m_Environment;
        private readonly IMixedSpaceCloudCodec m_Codec;

        public MixedSpaceCloudRequestFactory(IMixedSpaceNetworkEnvironment environment, IMixedSpaceCloudCodec codec)
        {
            m_Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            m_Codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public HttpRequestMessage Post<T>(T request, string baseUrl)
        {
            string json = m_Codec.Encode(request);
            string server = string.IsNullOrWhiteSpace(baseUrl) ? m_Environment.LocalizationServer : baseUrl;
            var message = new HttpRequestMessage(HttpMethod.Post, string.Format(UrlFormat, server, Endpoint<T>()));
            try { message.Content = new StringContent(json); return message; }
            catch { message.Dispose(); throw; }
        }

        public HttpRequestMessage Get(string uri) => new HttpRequestMessage(HttpMethod.Get, uri);

        public HttpRequestMessage Upload<T>(T request, byte[] data, IProgress<float> progress)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            byte[] metadata = Encoding.ASCII.GetBytes(m_Codec.Encode(request));
            // Metadata + NUL separator + exact binary payload, including empty data.
            var body = new byte[checked(metadata.Length + 1 + data.Length)];
            Buffer.BlockCopy(metadata, 0, body, 0, metadata.Length);
            Buffer.BlockCopy(data, 0, body, metadata.Length + 1, data.Length);
            var message = m_Environment.CreateUploadRequest(Endpoint<T>()) ??
                throw new InvalidOperationException("The upload environment returned no request.");
            var stream = new UploadProgressStream(body, progress);
            try
            {
                message.Content = new StreamContent(stream);
                message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return message;
            }
            catch { stream.Dispose(); message.Dispose(); throw; }
        }

        private static string Endpoint<T>()
        {
            var field = typeof(T).GetField("endpoint", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            string endpoint = field?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException(typeof(T).FullName + " must expose a public static endpoint string.");
            return endpoint;
        }
    }

    /// <summary>Upload progress measures consumed body bytes, not download bytes.</summary>
    internal sealed class UploadProgressStream : Stream
    {
        private readonly MemoryStream m_Body;
        private readonly IProgress<float> m_Progress;
        private readonly int m_BodyLength;
        public UploadProgressStream(byte[] body, IProgress<float> progress)
        {
            m_Body = new MemoryStream(body, false);
            m_BodyLength = body.Length;
            m_Progress = progress;
        }

        public override int Read([In, Out] byte[] buffer, int offset, int count)
        {
            int read = m_Body.Read(buffer, offset, count);
            Report();
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            int read = await m_Body.ReadAsync(buffer, offset, count, token).ConfigureAwait(false);
            Report();
            return read;
        }

        // StreamContent.CopyToAsync must pass through our reads. Inheriting
        // MemoryStream's optimized CopyToAsync bypasses a Read-only override.
        private void Report() => m_Progress?.Report(m_BodyLength == 0 ? 1f : (float)Position / m_BodyLength);
        public override bool CanRead => m_Body.CanRead;
        public override bool CanSeek => m_Body.CanSeek;
        public override bool CanWrite => false;
        public override long Length => m_Body.Length;
        public override long Position { get => m_Body.Position; set => m_Body.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => m_Body.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) m_Body.Dispose();
            base.Dispose(disposing);
        }
    }
}
