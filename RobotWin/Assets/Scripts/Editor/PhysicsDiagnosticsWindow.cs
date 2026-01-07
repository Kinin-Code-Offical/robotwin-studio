using UnityEditor;
using UnityEngine;
using RobotTwin.Game;

namespace RobotTwin.EditorTools
{
    public class PhysicsDiagnosticsWindow : EditorWindow
    {
        [MenuItem("RobotWin/Tools/Physics/Diagnostics")]
        private static void Open()
        {
            GetWindow<PhysicsDiagnosticsWindow>("Physics Diagnostics");
        }

        private void OnGUI()
        {
            var world = NativePhysicsWorld.Instance;
            if (world == null)
            {
                EditorGUILayout.HelpBox("NativePhysicsWorld not running. Enter Play Mode to view diagnostics.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Bodies", world.BodyCount.ToString());
            EditorGUILayout.LabelField("Last Step (ms)", world.LastStepMs.ToString("0.00"));
            EditorGUILayout.LabelField("Last Step dt (s)", world.LastStepDt.ToString("0.0000"));
            EditorGUILayout.LabelField("Last Substeps", world.LastStepSubsteps.ToString());
            EditorGUILayout.LabelField("Step Count", world.StepCount.ToString());

            GUILayout.Space(8f);
            if (GUILayout.Button("Apply Physics Config"))
            {
                world.ApplyConfig();
            }
        }
    }
}
