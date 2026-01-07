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

        public float TopSurfaceY => Thickness * 0.5f;
    }
}
