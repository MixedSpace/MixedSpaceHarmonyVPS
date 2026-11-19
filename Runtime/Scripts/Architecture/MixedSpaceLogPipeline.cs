using System;
using System.Text.RegularExpressions;

namespace MixedSpace
{
    internal enum MixedSpaceLogSeverity { Information, Warning, Error }

    /// <summary>Only sanitized text and a file basename may reach an output sink.</summary>
    internal readonly struct MixedSpaceLogRecord
    {
        public MixedSpaceLogSeverity Severity { get; }
        public string Source { get; }
        public string Message { get; }

        internal MixedSpaceLogRecord(MixedSpaceLogSeverity severity, string source, string message)
        {
            Severity = severity;
            Source = source;
            Message = message;
        }
    }

    internal interface IMixedSpaceLogSink
    {
        void Emit(MixedSpaceLogRecord record);
    }

    /// <summary>
    /// Synchronous processing with no queues, Unity objects or retained request
    /// payloads. A failing diagnostic sink must not break localization.
    /// </summary>
    internal sealed class MixedSpaceLogPipeline
    {
        private readonly Func<MixedSpaceLogger.LoggingLevel> m_Threshold;
        private readonly IMixedSpaceLogSink m_Sink;

        public MixedSpaceLogPipeline(Func<MixedSpaceLogger.LoggingLevel> threshold, IMixedSpaceLogSink sink)
        {
            m_Threshold = threshold ?? throw new ArgumentNullException(nameof(threshold));
            m_Sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public void Publish(MixedSpaceLogSeverity severity, MixedSpaceLogger.LoggingLevel level,
            string message, string callerPath)
        {
            // Preserve numeric filtering independently of Unity console severity.
            var record = new MixedSpaceLogRecord(severity,
                MixedSpaceLogText.SourceName(callerPath), MixedSpaceLogText.Redact(message));
            try { MixedSpaceRuntimeLog.ReceiveSdk(record); } catch { }
            if (m_Threshold() > level) return;
            bool previousForwarding = MixedSpaceRuntimeLog.ForwardingToConsole;
            try
            {
                MixedSpaceRuntimeLog.ForwardingToConsole = true;
                m_Sink.Emit(record);
            }
            catch
            {
                // 输出失败时不递归记录日志。
                // Never recursively log an output failure.
            }
            finally { MixedSpaceRuntimeLog.ForwardingToConsole = previousForwarding; }
        }
    }

    /// <summary>
    /// Text policy, not a credential vault: mask recognized credential fields and
    /// authorization forms. Unlabeled secrets cannot be inferred. Cloud callers
    /// must therefore never submit request or response bodies for logging.
    /// </summary>
    internal static class MixedSpaceLogText
    {
        private static readonly TimeSpan MatchBudget = TimeSpan.FromMilliseconds(50);
        private static readonly Regex Authorization = new Regex(
            @"\b(Bearer|Basic)\s+[^\s,""'<>]+", RegexOptions.IgnoreCase, MatchBudget);
        private static readonly Regex Credentials = new Regex(
            @"([""']?\b(?:m_)?(?:developer[_-]?token|access[_-]?token|refresh[_-]?token|token|authorization|password|secret|api[_-]?key)[""']?\s*[:=]\s*)(?:""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^\s,;&}]+)",
            RegexOptions.IgnoreCase, MatchBudget);
        private static readonly Regex UserInfo = new Regex(
            @"(https?://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase, MatchBudget);

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            try
            {
                text = Authorization.Replace(text, "$1 [redacted]");
                text = Credentials.Replace(text, match => match.Groups[1].Value + "[redacted]");
                return UserInfo.Replace(text, "$1[redacted]@");
            }
            catch (RegexMatchTimeoutException)
            {
                return "[log suppressed: redaction time budget exceeded]";
            }
        }

        public static string SourceName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "MixedSpace";
            string name = path.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            int extension = name.LastIndexOf('.');
            return extension > 0 ? name.Substring(0, extension) : name;
        }

        public static string Format(MixedSpaceLogRecord record) =>
            "[" + record.Source + "] " + record.Message;
    }

    /// <summary>
    /// Process-native callback boundary. The supervisor retains the delegate for
    /// its registered lifetime; native callers never depend on a scene component.
    /// Managed diagnostic failures must not unwind across the unmanaged ABI.
    /// </summary>
    internal static class MixedSpaceNativeLogBridge
    {
        [AOT.MonoPInvokeCallback(typeof(LogCallback))]
        public static void Receive(IntPtr ansiString)
        {
            if (ansiString == IntPtr.Zero) return;
            try
            {
                string message = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ansiString);
                MixedSpaceLogger.Log("Plugin: " + message,
                    MixedSpaceLogger.LoggingLevel.Verbose, "NativePlugin");
            }
            catch
            {
                // 托管异常不得逸出原生诊断回调。
                // No managed exception may escape a native diagnostic callback.
            }
        }
    }

    /// <summary>Unity console integration is the outermost output boundary.</summary>
    internal sealed class MixedSpaceUnityLogSink : IMixedSpaceLogSink
    {
        public void Emit(MixedSpaceLogRecord record)
        {
            string text = MixedSpaceLogText.Format(record);
            switch (record.Severity)
            {
                case MixedSpaceLogSeverity.Error: UnityEngine.Debug.LogError(text); break;
                case MixedSpaceLogSeverity.Warning: UnityEngine.Debug.LogWarning(text); break;
                default: UnityEngine.Debug.Log(text); break;
            }
        }
    }
}
