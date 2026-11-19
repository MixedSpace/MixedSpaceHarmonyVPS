// Augmented-image records, on top of the AREngineBridge.
//
// The old layer kept a raw trackable pointer per image and asked AREngine for one
// field at a time - a pose object to fill in, a float to write, a string to
// allocate. The bridge addresses images by their session-wide trackable id and
// already returns the whole record, so each entry here reads the same
// TryGetImageInfo call and picks its field out of it. The extra native call the
// old code avoided is now the cheaper option: the bridge's record buffer is
// reused, so a repeated read costs nothing but the id lookup.
//
// The handle a caller passes in is the bridge id shifted by one - see ARBridge -
// which is what lets an image cached in an earlier frame still read correctly.

namespace HuaweiARInternal
{
    using System;
    using UnityEngine;
    using AREngineBridge;

    internal class ARAugmentedImageAdapter
    {
        public ARAugmentedImageAdapter(NDKSession session)
        {
            // The images live in the current frame, which the bridge exposes
            // statically, so this adapter needs no session handle of its own -
            // NDKSession builds every adapter the same way, so the signature stays.
        }

        public Pose GetCenterPose(IntPtr AugImgHandle)
        {
            AREngineTrackedImage image;
            if (!AREngineSession.TryGetImageInfo(ARBridge.Id(AugImgHandle), out image))
            {
                return Pose.identity;
            }
            return ARBridge.ToUnityPose(image.centerPose);
        }

        public float GetExtentX(IntPtr AugImgHandle)
        {
            AREngineTrackedImage image;
            return AREngineSession.TryGetImageInfo(ARBridge.Id(AugImgHandle), out image) ? image.extentX : 0f;
        }

        public float GetExtentZ(IntPtr AugImgHandle)
        {
            AREngineTrackedImage image;
            return AREngineSession.TryGetImageInfo(ARBridge.Id(AugImgHandle), out image) ? image.extentZ : 0f;
        }

        public int GetDataBaseIndex(IntPtr AugImgHandle)
        {
            AREngineTrackedImage image;
            return AREngineSession.TryGetImageInfo(ARBridge.Id(AugImgHandle), out image) ? image.databaseIndex : 0;
        }

        public string AcquireName(IntPtr AugImgHandle)
        {
            // Empty rather than null when the id is unknown: the old import left
            // ImgName untouched on failure, and callers only log and display it.
            string ImgName = AREngineSession.GetTrackedImageNameById(ARBridge.Id(AugImgHandle));
            ARDebug.LogInfo("AcquireName:{0}", ImgName);
            return ImgName;
        }
    }
}
