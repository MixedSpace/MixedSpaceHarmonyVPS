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
    public class CaptureCurrentFrameStandardized : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Button captureButton;

        [Header("Settings")]
        [SerializeField] private string saveFolderName = "HWFrameCaptureStd";

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
                Debug.LogWarning("Failed to get intrinsics, writing default values.");
            }

            Pose pose;
            bool hasPose = m_hwSupport.TryGetCameraPose(out pose);
            if (!hasPose)
            {
                Debug.LogWarning("Failed to get camera pose, writing default values.");
            }

            Matrix4x4 proj;
            bool hasProj = m_hwSupport.TryGetProjectionMatrix(0.01f, 1000f, out proj);
            if (!hasProj)
            {
                Debug.LogWarning("Failed to get projection matrix, writing identity matrix.");
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_ff");
            string dir = Path.Combine(m_SaveDirectory, timestamp);
            Directory.CreateDirectory(dir);

            string imagePath = Path.Combine(dir, "frame.png");
            File.WriteAllBytes(imagePath, tex.EncodeToPNG());

            string json = JsonUtility.ToJson(new StandardizedFrameData
            {
                timestamp = timestamp,
                width = tex.width,
                height = tex.height,
                cameraPosition = new SerializableVector3(pose.position.x, pose.position.y, pose.position.z),
                cameraRotation = new SerializableQuaternion(pose.rotation.x, pose.rotation.y, pose.rotation.z, pose.rotation.w),
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
            }, true);

            string jsonPath = Path.Combine(dir, "frame.json");
            File.WriteAllText(jsonPath, json);

            string txt = string.Join(
                "\n",
                new[]
                {
                    "timestamp=" + timestamp,
                    "width=" + tex.width,
                    "height=" + tex.height,
                    "position=" + pose.position.x + "," + pose.position.y + "," + pose.position.z,
                    "rotation=" + pose.rotation.x + "," + pose.rotation.y + "," + pose.rotation.z + "," + pose.rotation.w,
                    "fx=" + intrinsics.fx,
                    "fy=" + intrinsics.fy,
                    "cx=" + intrinsics.cx,
                    "cy=" + intrinsics.cy,
                    "projection=" + proj.ToString()
                });

            string txtPath = Path.Combine(dir, "frame.txt");
            File.WriteAllText(txtPath, txt);

            Debug.Log("Saved standardized frame capture to: " + dir);
            Debug.Log("Image: " + imagePath);
            Debug.Log("JSON: " + jsonPath);
            Debug.Log("TXT: " + txtPath);
        }

        [Serializable]
        public class StandardizedFrameData
        {
            public string timestamp;
            public int width;
            public int height;
            public SerializableVector3 cameraPosition;
            public SerializableQuaternion cameraRotation;
            public SerializableIntrinsics intrinsics;
            public SerializableMatrix4x4 projectionMatrix;
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
