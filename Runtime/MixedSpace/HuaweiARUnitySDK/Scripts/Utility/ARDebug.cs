//#define __DEBUG__
namespace HuaweiARInternal
{
    using System;
    using UnityEngine;

    public class ARDebug
    {

        private static string DefaultLogTag = "arengine_unity ";

        public static void LogInfo(UnityEngine.Object message)
        {
            Debug.Log(message);
        }
        public static void LogInfo(string format, params object[] args)
        {
            Debug.LogFormat(DefaultLogTag + format, args);
        }

        public static void LogInfo(string tag, string format, params object[] args)
        {
            Debug.LogFormat(format, args);
        }

        public static void LogWarning(UnityEngine.Object message)
        {
            Debug.LogWarning(message);

        }
        public static void LogWarning(string format, params object[] args)
        {
            //Debug.LogWarningFormat(format, args);

        }

        public static void LogError(UnityEngine.Object message)
        {
            Debug.LogError(message);

        }
        public static void LogError(string format, params object[] args)
        {
            Debug.LogErrorFormat(format, args);

        }

    }
}
