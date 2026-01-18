using UnityEngine;

namespace RobotTwin.Tools
{
    public static class RunInBackgroundPreference
    {
        public const string RunInBackgroundKey = "RobotWin.Simulation.RunInBackground";

        public static bool Enabled
        {
            get
            {
                if (!PlayerPrefs.HasKey(RunInBackgroundKey))
                {
#if UNITY_EDITOR
                    bool editorValue = UnityEditor.EditorPrefs.GetBool(RunInBackgroundKey, false);
                    PlayerPrefs.SetInt(RunInBackgroundKey, editorValue ? 1 : 0);
                    PlayerPrefs.Save();
#endif
                }
                return PlayerPrefs.GetInt(RunInBackgroundKey, 0) == 1;
            }
            set
            {
                PlayerPrefs.SetInt(RunInBackgroundKey, value ? 1 : 0);
                PlayerPrefs.Save();
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(RunInBackgroundKey, value);
#endif
            }
        }

        public static void Apply()
        {
            Application.runInBackground = Enabled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ApplyOnLoad()
        {
            Apply();
        }
    }
}
