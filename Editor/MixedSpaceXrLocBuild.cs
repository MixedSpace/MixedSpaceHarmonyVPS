using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MixedSpace.Editor
{
    /// <summary>
    /// 检查鸿蒙原生库与 Windows 编辑器依赖的导入设置。
    /// Validates HarmonyOS native libraries and Windows Editor dependencies.
    /// </summary>
    internal sealed class MixedSpaceXrLocBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1010;
        public void OnPreprocessBuild(BuildReport report)
        {
            RepairBundledNativeImporters();
            var issues = CollectIssues(report.summary.platform);
            if (issues.Count > 0) throw new BuildFailedException(string.Join("\n", issues));
        }

        internal static void RepairBundledNativeImporters()
        {
            const string runtimePrefix = "Packages/com.njyjxr.mixedspaceharmony/Runtime/";
            var prefixes = new[] { runtimePrefix + "Plugins/", runtimePrefix + "MixedSpace/Plugins/" };
            var targets = new List<BuildTarget> { BuildTarget.OpenHarmony, BuildTarget.Android, BuildTarget.iOS,
                BuildTarget.StandaloneWindows, BuildTarget.StandaloneWindows64, BuildTarget.StandaloneOSX,
                BuildTarget.StandaloneLinux64, BuildTarget.WebGL, BuildTarget.WSAPlayer, BuildTarget.tvOS };
            if (Enum.TryParse("VisionOS", out BuildTarget visionTarget)) targets.Add(visionTarget);
            foreach (var plugin in PluginImporter.GetAllImporters())
            {
                string prefix = prefixes.FirstOrDefault(candidate => plugin.assetPath.StartsWith(candidate, StringComparison.Ordinal));
                bool harmony = plugin.assetPath.StartsWith(runtimePrefix, StringComparison.Ordinal) &&
                    plugin.assetPath.Contains("/OpenHarmony/") && plugin.assetPath.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
                if (prefix == null && !harmony) continue;
                string relative = plugin.assetPath.Substring(harmony ? runtimePrefix.Length : prefix.Length);
                BuildTarget allowed;
                string cpu;
                bool editor = false;
                if (harmony)
                { allowed = BuildTarget.OpenHarmony; cpu = "ARM64"; }
                else if (relative.StartsWith("x86_64/", StringComparison.Ordinal) && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                { allowed = BuildTarget.StandaloneWindows64; cpu = "x86_64"; editor = true; }
                else continue;
                bool clearConstraints = harmony;
                bool changed = plugin.GetCompatibleWithAnyPlatform() || plugin.GetCompatibleWithEditor() != editor ||
                    (!editor && !string.IsNullOrEmpty(cpu) && plugin.GetPlatformData(allowed, "CPU") != cpu) ||
                    (editor && (plugin.GetEditorData("OS") != "Windows" || plugin.GetEditorData("CPU") != "x86_64")) ||
                    (clearConstraints && plugin.DefineConstraints.Length != 0);
                foreach (var target in targets)
                    changed |= plugin.GetCompatibleWithPlatform(target) != (!editor && target == allowed);
                if (!changed) continue;
                plugin.SetCompatibleWithAnyPlatform(false);
                plugin.SetCompatibleWithEditor(editor);
                foreach (var target in targets) plugin.SetCompatibleWithPlatform(target, !editor && target == allowed);
                if (!editor && !string.IsNullOrEmpty(cpu)) plugin.SetPlatformData(allowed, "CPU", cpu);
                if (editor)
                {
                    plugin.SetEditorData("OS", "Windows");
                    plugin.SetEditorData("CPU", "x86_64");
                }
                if (clearConstraints) plugin.DefineConstraints = Array.Empty<string>();
                plugin.SaveAndReimport();
                UnityEngine.Debug.Log($"MixedSpace: repaired {relative} -> {allowed}/{cpu}, Editor={editor}.");
            }
        }

        private static List<string> CollectHarmonyIssues()
        {
            var issues = new List<string>();
            var importers = PluginImporter.GetAllImporters();
            string[] required = { "libAREngineBridge.so", "libHwAREngineUnityApi.so", "libxrloc.so", "libpose_fusion.so", "libomp.so" };
            foreach (string name in required)
            {
                var enabled = importers.Where(plugin => plugin.assetPath.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) &&
                    plugin.GetCompatibleWithPlatform(BuildTarget.OpenHarmony)).ToArray();
                if (enabled.Length != 1)
                    issues.Add($"Expected exactly one OpenHarmony ARM64 {name}; found {enabled.Length}.");
                foreach (var plugin in enabled)
                    if (plugin.GetCompatibleWithAnyPlatform() || plugin.GetCompatibleWithEditor() ||
                        plugin.GetPlatformData(BuildTarget.OpenHarmony, "CPU") != "ARM64" || plugin.DefineConstraints.Length != 0)
                        issues.Add($"{plugin.assetPath}: expected OpenHarmony ARM64 only, no Editor or Define Constraints.");
            }
            foreach (var plugin in importers.Where(plugin => plugin.assetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                if (plugin.GetCompatibleWithPlatform(BuildTarget.OpenHarmony))
                    issues.Add($"{plugin.assetPath}: a Windows DLL must not enter an OpenHarmony build.");
            return issues;
        }

        // 窗口和构建前复用只读检查。
        // The window and build share read-only importer checks.
        internal static List<string> CollectIssues(BuildTarget target)
        {
            return target == BuildTarget.OpenHarmony ? CollectHarmonyIssues() :
                new List<string> { "MixedSpace Harmony SDK supports OpenHarmony builds only." };
        }

    }
}
