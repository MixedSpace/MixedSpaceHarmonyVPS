using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace MixedSpace.Editor
{
    /// <summary>Harmony sample setup, following the optional Rokid setup-window workflow.</summary>
    public sealed class MixedSpaceHarmonySetupWindow : EditorWindow
    {
        private const string PendingKey = "MixedSpace.Harmony.SetupPending";
        private const string PackageName = "com.njyjxr.mixedspaceharmony";
        private static readonly string[] Permissions = {
            "ohos.permission.CAMERA", "ohos.permission.GYROSCOPE", "ohos.permission.ACCELEROMETER",
            "ohos.permission.APPROXIMATELY_LOCATION", "ohos.permission.LOCATION" };
        private static readonly string[] TmpResourceGuids =
        {
            "3f5b5dff67a942289a9defa416b206f3",
            "8f586378b4e144a9851e7b34d9b748ee",
            "e3265ab4bf004d28a9537516768c1c75"
        };
        private static AddAndRemoveRequest s_Install;
        private Vector2 m_Scroll;
        private string m_Error;

        [MenuItem("MixedSpaceSDK/Adapters/Harmony 设置与依赖检查")]
        public static void Open() => GetWindow<MixedSpaceHarmonySetupWindow>("Harmony 设置");

        [InitializeOnLoadMethod]
        private static void Initialize() => EditorApplication.delayCall += OpenPending;

        internal static void OpenPending()
        {
            if (Application.isBatchMode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall -= OpenPending;
                EditorApplication.delayCall += OpenPending;
                return;
            }
            if (MixedSpaceHarmonySetupImportNotice.RememberExistingSample())
                SessionState.SetBool(PendingKey, false);
            if (!SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            Open();
        }

        private void OnInspectorUpdate()
        {
            if (s_Install != null && s_Install.IsCompleted)
            {
                m_Error = s_Install.Status == StatusCode.Success ? null :
                    s_Install.Error?.message ?? "依赖导入失败，请查看 Package Manager。";
                s_Install = null;
            }
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("MixedSpace Harmony SDK", EditorStyles.boldLabel);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            bool busy = s_Install != null || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(busy))
            {
                var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                var package = installed.FirstOrDefault(p => p.name == PackageName);
                if (package == null)
                    EditorGUILayout.HelpBox("未找到 MixedSpace UPM 包，请从 Package Manager 添加本包的 package.json。", MessageType.Error);
                else
                {
                    foreach (var dependency in package.dependencies ?? Array.Empty<DependencyInfo>())
                    {
                        if (installed.Any(p => p.name == dependency.name)) continue;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(dependency.name);
                            if (GUILayout.Button("导入", GUILayout.Width(60)))
                                TryRun(() => ImportDependency(dependency.name, dependency.version));
                        }
                    }
                }
                if (package != null && (package.dependencies ?? Array.Empty<DependencyInfo>()).All(
                    dependency => installed.Any(p => p.name == dependency.name)))
                    EditorGUILayout.HelpBox("本包声明的 UPM 依赖均已安装，已安装项会自动隐藏。", MessageType.Info);
                if (installed.Any(p => p.name == "com.unity.textmeshpro") &&
                    !HasTmpResources())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("TMP Essential Resources");
                        if (GUILayout.Button("导入", GUILayout.Width(60)))
                            TryRun(ImportTmpResources);
                    }
                }
                if (GUILayout.Button("配置鸿蒙设置")) TryRun(Configure);
                if (GUILayout.Button("打开示例场景")) TryRun(OpenSample);
                if (GUILayout.Button("打开 Player Settings"))
                    SettingsService.OpenProjectSettings("Project/Player");
            }
            if (!string.IsNullOrEmpty(m_Error)) EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        private static bool HasTmpResources()
        {
            return TmpResourceGuids.All(guid =>
                !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)));
        }

        private static void ImportTmpResources()
        {
            if (!EditorApplication.ExecuteMenuItem(
                "Window/TextMeshPro/Import TMP Essential Resources"))
            {
                throw new InvalidOperationException(
                    "TMP 官方资源导入入口不可用，请检查 TextMeshPro 包。");
            }
        }

        private static void ImportDependency(string packageName, string version)
        {
            if (s_Install != null) return;
            s_Install = Client.AddAndRemove(new[] { packageName + "@" + version });
        }

        private void TryRun(Action action)
        {
            try { m_Error = null; action(); }
            catch (Exception exception) { m_Error = exception.Message; Debug.LogException(exception); }
        }

        private static void Configure()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.OpenHarmony &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.OpenHarmony, BuildTarget.OpenHarmony))
                throw new InvalidOperationException("切换失败，请检查团结引擎 OpenHarmony 构建模块。");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.OpenHarmony, ScriptingImplementation.IL2CPP);
            PlayerSettings.OpenHarmony.targetArchitectures = OpenHarmonyArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.OpenHarmony, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.OpenHarmony, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.OpenHarmony.compatibleSdkVersion = Math.Max(14, PlayerSettings.OpenHarmony.compatibleSdkVersion);
            PlayerSettings.OpenHarmony.openHarmonyPredefinedPermissions =
                (PlayerSettings.OpenHarmony.openHarmonyPredefinedPermissions ?? Array.Empty<string>()).Union(Permissions).ToArray();
            PlayerSettings.OpenHarmony.forceInternetPermission = true;
            MixedSpaceXrLocBuild.RepairBundledNativeImporters();
            AssetDatabase.SaveAssets();
        }

        private static void OpenSample()
        {
            string path = AssetDatabase.GUIDToAssetPath("263be312feaec4e45a3c2b4313da3236");
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("请先在 Package Manager 的 Samples 中导入 HarmonyAdapter。");
            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
        }
    }

    internal sealed class MixedSpaceHarmonySetupImportNotice : AssetPostprocessor
    {
        private const string SceneGuid = "263be312feaec4e45a3c2b4313da3236";
        private static string SeenKey => "MixedSpace.Harmony.SetupSeen." + Application.dataPath + "." + SceneGuid;

        internal static bool RememberExistingSample()
        {
            if (EditorPrefs.HasKey(SeenKey)) return false;
            string path = AssetDatabase.GUIDToAssetPath(SceneGuid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            EditorPrefs.SetString(SeenKey, path);
            return true;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
        {
            if (Application.isBatchMode) return;
            if (deleted.Contains(EditorPrefs.GetString(SeenKey, ""))) EditorPrefs.DeleteKey(SeenKey);
            foreach (string path in imported)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".unity", StringComparison.Ordinal) ||
                    AssetDatabase.AssetPathToGUID(path) != SceneGuid || EditorPrefs.GetString(SeenKey, "") == path) continue;
                EditorPrefs.SetString(SeenKey, path);
                SessionState.SetBool("MixedSpace.Harmony.SetupPending", true);
                EditorApplication.delayCall -= MixedSpaceHarmonySetupWindow.OpenPending;
                EditorApplication.delayCall += MixedSpaceHarmonySetupWindow.OpenPending;
            }
        }
    }
}
