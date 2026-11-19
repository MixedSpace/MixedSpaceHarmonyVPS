namespace HuaweiARInternal
{
    using HuaweiARUnitySDK;
    using System;
    internal class ARExceptionAdapter
    {
        public static void ExtractException(NDKARStatus status)
        {
            switch (status)
            {
                case NDKARStatus.ARENGINE_SUCCESS:
                    return;
                default:
                    throw new ApplicationException("NOT SUCCESS---" + status);
            }

        }
    }
}
