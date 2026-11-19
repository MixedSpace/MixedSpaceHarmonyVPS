using System;
using System.IO;
using HuaweiARInternal;
using UnityEngine;
using UnityEngine.UI;
using HuaweiARUnitySDK;
using HuaweiARInternal;
using MixedSpace.XR;

namespace MixedSpace.XR
{
    public class CaptureCurrentFrame : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Button captureButton;

        [Header("Settings")]
        [SerializeField] private string saveFolderName = "HWFrameCapture";
        [SerializeField] private bool savePng = true;

        private string m_SaveDirectory;

        public HWSupport m_hwSupport;

        private void Start()
        {
            if (captureButton != null)
            {
                captureButton.onClick.AddListener(Capture);
            }

            m_SaveDirectory = Path.Combine(Application.persistentDataPath, saveFolderName);
            Directory.CreateDirectory(m_SaveDirectory);
        }

        public void Capture()
        {
            if (!m_hwSupport.IsSessionReady())
            {
                Debug.LogWarning("Huawei session is not ready.");
                return;
            }

            Texture2D tex = m_hwSupport.GetCameraTexture();
            if (tex == null)
            {
                Debug.LogWarning("Camera texture is null.");
                return;
            }

            ARCameraIntrinsics intrinsics;
            bool hasIntrinsics = m_hwSupport.TryGetCameraIntrinsics(out intrinsics);
            if (!hasIntrinsics)
            {
                Debug.LogWarning("Failed to get camera intrinsics.");
            }

            Pose cameraPose;
            bool hasPose = m_hwSupport.TryGetCameraPose(out cameraPose);
            if (!hasPose)
            {
                Debug.LogWarning("Failed to get camera pose.");
            }

            Matrix4x4 proj;
            bool hasProj = m_hwSupport.TryGetProjectionMatrix(0.01f, 1000f, out proj);
            if (!hasProj)
            {
                Debug.LogWarning("Failed to get projection matrix.");
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_ff");

            if (savePng)
            {
                byte[] pngBytes = tex.EncodeToPNG();
                string imagePath = Path.Combine(m_SaveDirectory, "frame_" + timestamp + ".png");
                File.WriteAllBytes(imagePath, pngBytes);
                Debug.Log("Saved image: " + imagePath);
            }

            FrameCaptureData frameCapture = new FrameCaptureData
            {
                timestamp = timestamp,
                width = tex.width,
                height = tex.height,
                cameraPose = new SerializablePose
                {
                    position = new SerializableVector3(cameraPose.position.x, cameraPose.position.y, cameraPose.position.z),
                    rotation = new SerializableQuaternion(cameraPose.rotation.x, cameraPose.rotation.y, cameraPose.rotation.z, cameraPose.rotation.w)
                },
                intrinsics = new SerializableIntrinsics
                {
                    fx = intrinsics.fx,
                    fy = intrinsics.fy,
                    cx = intrinsics.cx,
                    cy = intrinsics.cy,
                    width = intrinsics.width,
                    height = intrinsics.height
                },
                projectionMatrix = new SerializableMatrix4x4(proj)
            };

            string json = JsonUtility.ToJson(frameCapture, true);
            string jsonPath = Path.Combine(m_SaveDirectory, "frame_" + timestamp + ".json");
            File.WriteAllText(jsonPath, json);

            Debug.Log("Saved frame info: " + jsonPath);
        }

        [Serializable]
        public class FrameCaptureData
        {
            public string timestamp;
            public int width;
            public int height;
            public SerializablePose cameraPose;
            public SerializableIntrinsics intrinsics;
            public SerializableMatrix4x4 projectionMatrix;
        }

        [Serializable]
        public class SerializablePose
        {
            public SerializableVector3 position;
            public SerializableQuaternion rotation;
        }

        [Serializable]
        public class SerializableVector3
        {
            public float x;
            public float y;
            public float z;

            public SerializableVector3(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        [Serializable]
        public class SerializableQuaternion
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public SerializableQuaternion(float x, float y, float z, float w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }
        }

        [Serializable]
        public class SerializableIntrinsics
        {
            public float fx;
            public float fy;
            public float cx;
            public float cy;
            public int width;
            public int height;
        }

        [Serializable]
        public class SerializableMatrix4x4
        {
            public float[] values;

            public SerializableMatrix4x4(Matrix4x4 matrix)
            {
                values = new[]
                {
                    matrix.m00, matrix.m01, matrix.m02, matrix.m03,
                    matrix.m10, matrix.m11, matrix.m12, matrix.m13,
                    matrix.m20, matrix.m21, matrix.m22, matrix.m23,
                    matrix.m30, matrix.m31, matrix.m32, matrix.m33,
                };
            }
        }
    }
}
