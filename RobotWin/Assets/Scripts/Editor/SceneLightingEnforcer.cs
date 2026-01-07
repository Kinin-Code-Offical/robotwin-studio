using UnityEditor;

namespace RobotTwin.EditorTools
{
    [InitializeOnLoad]
    public static class SceneLightingEnforcer
    {
        static SceneLightingEnforcer()
        {
            EditorApplication.delayCall += EnableSceneLighting;
        }

        private static void EnableSceneLighting()
        {
            if (SceneView.sceneViews == null) return;
            foreach (var viewObj in SceneView.sceneViews)
            {
                if (viewObj is SceneView view)
                {
                    if (!view.sceneLighting)
                    {
                        view.sceneLighting = true;
                        view.Repaint();
                    }
                }
            }
        }
    }
}
