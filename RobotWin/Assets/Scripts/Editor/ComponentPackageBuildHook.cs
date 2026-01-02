using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotTwin.Editor
{
    public class ComponentPackageBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                ComponentPackageExporter.ExportPackages();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ComponentPackageBuildHook] Failed to export packages: {ex.Message}");
                throw new BuildFailedException("Component package export failed. See console for details.");
            }
        }
    }
}
