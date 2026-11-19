using System.Runtime.CompilerServices;

namespace MixedSpace
{
    /// <summary>
    /// Stable application logging entry point. Processing and output are separate
    /// roles; this process-wide diagnostic filter is not a per-session policy.
    /// </summary>
    public static class MixedSpaceLogger
    {
        public enum LoggingLevel { All = 0, Verbose = 1, ErrorsAndWarnings = 2, ErrorsOnly = 3, None = 4 }

        public static LoggingLevel Level = LoggingLevel.ErrorsAndWarnings;
        private static readonly MixedSpaceLogPipeline Pipeline =
            new MixedSpaceLogPipeline(() => Level, new MixedSpaceUnityLogSink());

        public static void Log(string message, LoggingLevel messageLevel = LoggingLevel.All,
            [CallerFilePath] string filePath = "") =>
            Pipeline.Publish(MixedSpaceLogSeverity.Information, messageLevel, message, filePath);

        public static void LogWarning(string message, [CallerFilePath] string filePath = "") =>
            Pipeline.Publish(MixedSpaceLogSeverity.Warning, LoggingLevel.ErrorsAndWarnings, message, filePath);

        public static void LogError(string message, [CallerFilePath] string filePath = "") =>
            Pipeline.Publish(MixedSpaceLogSeverity.Error, LoggingLevel.ErrorsOnly, message, filePath);
    }
}
