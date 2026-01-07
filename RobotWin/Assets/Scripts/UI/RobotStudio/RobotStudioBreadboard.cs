using UnityEngine;

namespace RobotTwin.UI
{
    public class RobotStudioBreadboard : MonoBehaviour
    {
        public string Id;
        public int PinCount;
        public int Columns;
        public int Rows;
        public float Pitch;
        public float Thickness;
        public float Margin;
        public float Width;
        public float Depth;
        public Renderer BoardRenderer;

        // Owned resources created at runtime (e.g., texture/material for the base).
        // These are not Unity asset references and must be cleaned up to avoid leaks.
        public Material OwnedMaterial;
        public Texture2D OwnedTexture;

        public float TopSurfaceY => Thickness * 0.5f;

        private void OnDestroy()
        {
            // If we authored the shared material/texture at runtime, we own lifetime.
            if (BoardRenderer != null && OwnedMaterial != null && BoardRenderer.sharedMaterial == OwnedMaterial)
            {
                BoardRenderer.sharedMaterial = null;
            }

            DestroyOwnedResource(OwnedMaterial);
            OwnedMaterial = null;

            DestroyOwnedResource(OwnedTexture);
            OwnedTexture = null;
        }

        private static void DestroyOwnedResource(Object resource)
        {
            if (resource == null) return;

            if (Application.isPlaying)
            {
                Destroy(resource);
            }
            else
            {
                DestroyImmediate(resource);
            }
        }
    }
}
