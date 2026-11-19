using System;
namespace HuaweiARUnitySDK
{
   [Flags]
    public enum ARConfigSemanticMode
    {
        NONE = 0,
        PLANE = 1 << 0,
        TAEGET = 1 << 1
    }

    public enum ARConfigSemanticDenseMode
    {
        /** Disable Semantic Dense. */
        DISABLED = 0,

        /** Normal Semantic Dense. */
       NORMAL = 1,

        /** Semantic Dense For Cube Volume. */
       VOLUME = 2,

        /** Semantic Dense For Cube Space. */
        SPACE = 3,
    }
}