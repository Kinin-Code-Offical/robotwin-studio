using System;

namespace RobotTwin.UI
{
    public static class ComponentStudioSession
    {
        public static string PayloadJson;
        public static string PackagePath;
        public static string SourceModelPath;
        public static bool IsNew;
        public static string ReturnScene = "Wizard";

        public static bool HasSession => !string.IsNullOrWhiteSpace(PayloadJson);

        public static void StartNew(string payloadJson, string packagePath, string sourceModelPath, string returnScene)
        {
            PayloadJson = payloadJson ?? string.Empty;
            PackagePath = packagePath ?? string.Empty;
            SourceModelPath = sourceModelPath ?? string.Empty;
            IsNew = true;
            ReturnScene = string.IsNullOrWhiteSpace(returnScene) ? "Wizard" : returnScene;
        }

        public static void StartEdit(string payloadJson, string packagePath, string returnScene)
        {
            PayloadJson = payloadJson ?? string.Empty;
            PackagePath = packagePath ?? string.Empty;
            SourceModelPath = string.Empty;
            IsNew = false;
            ReturnScene = string.IsNullOrWhiteSpace(returnScene) ? "Wizard" : returnScene;
        }

        public static void Clear()
        {
            PayloadJson = string.Empty;
            PackagePath = string.Empty;
            SourceModelPath = string.Empty;
            IsNew = false;
        }
    }
}
