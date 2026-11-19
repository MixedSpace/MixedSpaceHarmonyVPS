namespace Common
{
    using UnityEngine;
    using HuaweiARUnitySDK;
    using System.Collections.Generic;
    using HuaweiARInternal;
    using System.Text;

    public class FaceVisualizer : MonoBehaviour
    {
        private ARFace m_face;
        private StringBuilder m_sb = new StringBuilder();
        private int index = 0;
        Dictionary<string,string> m_blendShapes = new Dictionary<string,string>();

        public void Initialize(ARFace face, int i)
        {
            m_face = face;
            index = i;
            Update();
        }

        public void Update()
        {
            m_sb.Remove(0, m_sb.Length);
            if (null == m_face)
            {
                return;
            }
            else if (m_face.GetTrackingState() == ARTrackable.TrackingState.PAUSED)
            {
                //disable face renderer
                return;
            }
            else if (m_face.GetTrackingState() == ARTrackable.TrackingState.STOPPED)
            {
                Destroy(gameObject);
                return;
            }
            m_sb.Append("Face Pose: ");
            m_sb.Append("\n");
            m_sb.Append("position: ");
            m_sb.Append(m_face.GetPose().position.ToString());
            m_sb.Append("\n");
            m_sb.Append("rotation: ");
            m_sb.Append(m_face.GetPose().rotation.ToString());
            m_sb.Append("\n");

            var blendShapes = m_face.GetBlendShapeWithBlendName();
            // 防止识别不到人脸表情时日志会闪屏
            if (blendShapes.Count == 0 )
            {
                foreach (var item in m_blendShapes)
                {
                    m_sb.Append(item.Key);
                    m_sb.Append(": ");
                    m_sb.Append(item.Value);
                    m_sb.Append("\n");
                }
                return;
            }
            foreach (var bs in blendShapes)
            {
                string key = bs.Key.Replace("Animoji_", string.Empty);
                string value = bs.Value.ToString("F4");
                m_sb.Append(key);
                m_sb.Append(": ");
                m_sb.Append(value);
                m_sb.Append("\n");
                if (!m_blendShapes.ContainsKey(key))
                {
                    m_blendShapes.Add(key,"0.0000");
                }
            }

            var faceGeometry = m_face.GetFaceGeometry();
            m_sb.Append("Face Geometry triangle count: ");
            m_sb.Append(faceGeometry.TriangleCount);
            m_sb.Append("\n");
        }

        public void OnGUI()
        { 
            GUIStyle bb = new GUIStyle();
            bb.normal.background = null;
            bb.normal.textColor = new Color(1, 0, 0);
            bb.fontSize = 30;
            int width = index * 450;
            GUI.Label(new Rect(width, 100, 200, 200), m_sb.ToString(), bb);
        }
    }
}
