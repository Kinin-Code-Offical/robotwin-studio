using UnityEngine;

namespace RobotTwin.UI
{
    public class RobotStudioAssemblyItem : MonoBehaviour
    {
        public string InstanceId;
        public string ComponentType;
        public string CatalogId;
        public bool Pinned;
        public float DensityKgPerM3 = 1200f;
        public float Friction = 0.6f;
        public float MassKg;
        public GameObject PinIndicator;

        public void SetPinned(bool pinned)
        {
            Pinned = pinned;
            if (PinIndicator != null)
            {
                PinIndicator.SetActive(pinned);
            }
        }
    }
}
