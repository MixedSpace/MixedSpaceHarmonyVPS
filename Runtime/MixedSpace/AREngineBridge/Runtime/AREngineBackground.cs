// Draws the AREngine camera preview behind the scene and keeps a Unity camera in
// step with the tracked pose.
//
// This replaces the BackGroundRenderer + ARBackgroundRenderer pair from Huawei's
// Unity SDK. Both halves live here because the only thing the second one did was
// own a CommandBuffer, and splitting it meant the command buffer could outlive the
// material that fed it.

using UnityEngine;
using UnityEngine.Rendering;

namespace AREngineBridge
{
    /// <summary>
    ///     blits the camera preview as the background of the camera it is attached
    ///     to, and drives that camera from the tracked pose
    /// </summary>
    /// <remarks>
    ///     something else must call <see cref="AREngineSession.Update"/> earlier in
    ///     the frame; this component only reads AREngineSession.CurrentFrame and
    ///     draws whatever it finds there
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class AREngineBackground : MonoBehaviour
    {
        // Mirrors the quad UVs Huawei's SDK uses per lens facing. The front camera
        // is flipped so the preview is not mirrored.
        private static readonly float[] RearQuadUvs = { 0f, 1f, 0f, 0f, 1f, 1f, 1f, 0f };
        private static readonly float[] FrontQuadUvs = { 0f, 0f, 0f, 1f, 1f, 0f, 1f, 1f };

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int UvLeftTopBottomId = Shader.PropertyToID("_UvLeftTopBottom");
        private static readonly int UvRightTopBottomId = Shader.PropertyToID("_UvRightTopBottom");

        private readonly float[] _transformedUvs = new float[8];

        private Camera _camera;
        private CommandBuffer _commandBuffer;
        private Texture _blitSource;
        private CameraClearFlags _savedClearFlags;
        private bool _clearFlagsSaved;
        private bool _uvsValid;
        private bool _geometryInitialised;
        private ScreenOrientation _lastOrientation;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private float _clipNear = -1f;
        private float _clipFar = -1f;

        // Diagnostics for the "left/right triangle stretched along a diagonal"
        // symptom: a fullscreen Blit shows a seam on the quad diagonal whenever
        // TransformDisplayUvCoords returns a degenerate edge (two corners collapse
        // to one texel column). These cache the raw numbers so OnGUI / the log can
        // reveal which value is bad on-device without a rebuild.
        private bool _diagLogged;
        private GUIStyle _diagStyle;
        private bool _diagHaveUvs;
        private string _diagText = "waiting";

        /// <summary>
        ///     the material that samples the preview, using the AREngine/Background
        ///     shader
        /// </summary>
        public Material backgroundMaterial;

        /// <summary>
        ///     whether this component also moves the camera to the tracked pose and
        ///     assigns its projection matrix
        /// </summary>
        public bool driveCameraPose = true;

        /// <summary>
        ///     which camera the preview comes from; selects the quad UVs
        /// </summary>
        public AREngine_ARCameraLensFacing lensFacing =
            AREngine_ARCameraLensFacing.ARENGINE_CAMERA_FACING_REAR;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (backgroundMaterial == null)
            {
                return;
            }

            UpdateDisplayGeometry();

            var frame = AREngineSession.CurrentFrame;
            if (!frame.isValid)
            {
                return;
            }

            var texture = AREngineSession.GetCameraTexture();
            if (texture == null)
            {
                return;
            }

            backgroundMaterial.SetTexture(MainTexId, texture);
            UpdateUvs();

            if (driveCameraPose)
            {
                UpdateCameraPose(frame);
            }

            EnsureCommandBuffer(texture);
        }

        private void OnDisable()
        {
            RemoveCommandBuffer();
        }

        private void OnDestroy()
        {
            RemoveCommandBuffer();
        }

        private void UpdateDisplayGeometry()
        {
            if (_geometryInitialised && Screen.orientation == _lastOrientation &&
                Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight)
            {
                return;
            }

            _geometryInitialised = true;
            _lastOrientation = Screen.orientation;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            AREngineSession.SetDisplayGeometryFromScreen();

            // The preview transform depends on the geometry just pushed, so the
            // cached UVs are wrong until AREngine has consumed it.
            _uvsValid = false;
        }

        private void UpdateUvs()
        {
            if (!_uvsValid || AREngineSession.DisplayGeometryChanged)
            {
                var source = lensFacing == AREngine_ARCameraLensFacing.ARENGINE_CAMERA_FACING_FRONT
                    ? FrontQuadUvs
                    : RearQuadUvs;
                if (AREngineSession.TransformDisplayUvCoords(source, _transformedUvs) !=
                    AREngine_ARStatus.ARENGINE_SUCCESS)
                {
                    return;
                }

                _uvsValid = true;
                _diagHaveUvs = true;
                RefreshUvDiagnostic();
            }

            backgroundMaterial.SetVector(UvLeftTopBottomId,
                new Vector4(_transformedUvs[0], _transformedUvs[1], _transformedUvs[2], _transformedUvs[3]));
            backgroundMaterial.SetVector(UvRightTopBottomId,
                new Vector4(_transformedUvs[4], _transformedUvs[5], _transformedUvs[6], _transformedUvs[7]));
        }

        // Formats the current geometry + 8 transformed UVs into a diagnostic string
        // and logs it once, so the numbers survive into hilog as well as OnGUI.
        private void RefreshUvDiagnostic()
        {
            var tex = AREngineSession.GetCameraTexture();
            int texW = tex != null ? tex.width : -1;
            int texH = tex != null ? tex.height : -1;
            var u = _transformedUvs;
            _diagText =
                "orient=" + Screen.orientation + " rot=" + AREngineSession.RotationForOrientation(Screen.orientation) + "\n"
                + "screen=" + Screen.width + "x" + Screen.height + "  camTex=" + texW + "x" + texH + "\n"
                + "geoChanged=" + AREngineSession.DisplayGeometryChanged + "\n"
                + "Ltop=(" + F(u[0]) + "," + F(u[1]) + ") Lbot=(" + F(u[2]) + "," + F(u[3]) + ")\n"
                + "Rtop=(" + F(u[4]) + "," + F(u[5]) + ") Rbot=(" + F(u[6]) + "," + F(u[7]) + ")\n"
                + "leftEdgeSpan=" + F(Mathf.Abs(u[0] - u[2]) + Mathf.Abs(u[1] - u[3]))
                + " rightEdgeSpan=" + F(Mathf.Abs(u[4] - u[6]) + Mathf.Abs(u[5] - u[7]));

            if (_diagLogged)
            {
                return;
            }

            _diagLogged = true;
            Debug.Log("[AREngineBackground][uvdiag] " + _diagText.Replace('\n', '|'));
        }

        private static string F(float v)
        {
            return v.ToString("0.####");
        }

        // On-device panel mirroring the Huawei SDK's BackGroundRenderer diagnostics,
        // so the geometry / UV numbers can be read with the naked eye over a single
        // run instead of pulling a hilog capture.
        private void OnGUI()
        {
            if (!AREngineSession.IsCreated)
            {
                return;
            }

            if (_diagStyle == null)
            {
                _diagStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                };
                _diagStyle.normal.textColor = Color.yellow;
            }

            GUI.Label(new Rect(10, 10, Screen.width - 20, 600),
                _diagHaveUvs ? _diagText : "uv diag: awaiting first transformed UVs", _diagStyle);
        }

        private void UpdateCameraPose(AREngineFrame frame)
        {
            _camera.transform.position = frame.cameraPose.position;
            _camera.transform.rotation = frame.cameraPose.rotation;

            // The bridge bakes the clip planes into the projection matrix when it
            // builds it, so push them before the next frame rather than after this one.
            if (!Mathf.Approximately(_camera.nearClipPlane, _clipNear) ||
                !Mathf.Approximately(_camera.farClipPlane, _clipFar))
            {
                _clipNear = _camera.nearClipPlane;
                _clipFar = _camera.farClipPlane;
                AREngineSession.SetClipPlanes(_clipNear, _clipFar);
            }

            _camera.projectionMatrix = frame.projectionMatrix;
        }

        private void EnsureCommandBuffer(Texture texture)
        {
            if (_commandBuffer != null && _blitSource == texture)
            {
                return;
            }

            RemoveCommandBuffer();
            _blitSource = texture;

            if (!_clearFlagsSaved)
            {
                _savedClearFlags = _camera.clearFlags;
                _clearFlagsSaved = true;
            }

            _camera.clearFlags = CameraClearFlags.Depth;
            _commandBuffer = new CommandBuffer { name = "AREngine background" };
            _commandBuffer.Blit(texture, BuiltinRenderTextureType.CameraTarget, backgroundMaterial);

            // Both events, because which one runs depends on the render path and a
            // background missing on one of them shows up as a black screen.
            _camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _commandBuffer);
            _camera.AddCommandBuffer(CameraEvent.BeforeGBuffer, _commandBuffer);
        }

        private void RemoveCommandBuffer()
        {
            if (_commandBuffer == null)
            {
                return;
            }

            if (_camera != null)
            {
                _camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, _commandBuffer);
                _camera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, _commandBuffer);
                if (_clearFlagsSaved)
                {
                    _camera.clearFlags = _savedClearFlags;
                }
            }

            _commandBuffer.Dispose();
            _commandBuffer = null;
            _blitSource = null;
            _clearFlagsSaved = false;
        }
    }
}
