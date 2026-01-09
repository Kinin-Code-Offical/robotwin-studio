using UnityEngine;

namespace RobotWin.Environment
{
    /// <summary>
    /// Procedurally alters the localized friction or geometry of a track 
    /// to simulate real-world defects (e.g. Tape Seams, Wet Spots).
    /// </summary>
    [RequireComponent(typeof(MeshCollider))]
    public class SurfaceImperfection : MonoBehaviour
    {
        [Header("Imperfection Settings")]
        [Tooltip("Every X meters, introduce a bump.")]
        public float seamInterval = 0.42f; // A3 paper height approx
        
        [Tooltip("Height of the tape seam bump in meters.")]
        public float bumpHeight = 0.0005f; // 0.5mm tape thickness

        [Tooltip("Chance (0-1) that the robot loses grip at a seam.")]
        public float slipChance = 0.1f;

        // Unity Physics Materials
        private PhysicsMaterial _paperMat;
        private PhysicsMaterial _tapeMat;

        void Start()
        {
            SetupMaterials();
        }

        private void SetupMaterials()
        {
            _paperMat = new PhysicsMaterial("Paper");
            _paperMat.dynamicFriction = 0.8f;
            _paperMat.staticFriction = 0.9f;

            _tapeMat = new PhysicsMaterial("Tape");
            _tapeMat.dynamicFriction = 0.6f; // Smoother
            _tapeMat.staticFriction = 0.65f;
        }

        void OnCollisionStay(Collision collision)
        {
            // Simple logic: if robot runs over a "seam" coordinate
            // We momentarily swap the physics material to something slippery or "bumpy"
            
            // Note: In a full implementation, this would likely be done via a Shader 
            // or by modifying the TerrainData, but using Collider logic for the "Digital Twin" MVP.
            
            // For now, let's inject a tiny force to simulate the "Bump" of an A3 sheet edge
            // A3 height os roughly 420mm. 
            
            float zPos = collision.transform.position.z;
            float remainder = Mathf.Abs(zPos % seamInterval);

            // If we are within 1cm of a seam
            if (remainder < 0.01f)
            {
                Rigidbody rb = collision.rigidbody;
                if (rb != null)
                {
                    // Adding a tiny vertical impulse to simulate driving over the tape edge
                    rb.AddForce(Vector3.up * bumpHeight * 100f, ForceMode.Impulse);
                }
            }
        }
    }
}
