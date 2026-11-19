using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ARFaceMultiMode
{
    /** Disable multi-face detection mode */
    MULTIFACE_DISABLE = 0x300,

    /** Enable multi-face detection mode */
    MULTIFACE_ENABLE = 0x800
}

public enum ARCameraLensFacing
{
    /** Rear camera. */
    CAMERA_FACING_REAR = 0,

    /** Front camera. */
    CAMERA_FACING_FRONT = 1
}