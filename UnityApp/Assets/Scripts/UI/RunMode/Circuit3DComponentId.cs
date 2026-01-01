using UnityEngine;

namespace RobotTwin.UI
{
    public class Circuit3DComponentId : MonoBehaviour
    {
        [SerializeField] private string _componentId;
        [SerializeField] private string _componentType;

        public string ComponentId => _componentId;
        public string ComponentType => _componentType;

        public void Initialize(string componentId, string componentType)
        {
            _componentId = componentId;
            _componentType = componentType;
        }
    }
}
