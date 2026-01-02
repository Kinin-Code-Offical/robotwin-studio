using UnityEngine;

namespace RobotTwin.UI
{
    public static class CircuitLayoutSizing
    {
        public const float BoardWorldWidth = 4000f;
        public const float BoardWorldHeight = 2400f;
        public const float DefaultComponentWidth = 120f;
        public const float DefaultComponentHeight = 56f;

        public static Vector2 GetComponentSize2D(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return new Vector2(DefaultComponentWidth, DefaultComponentHeight);
            }

            var catalogItem = ComponentCatalog.GetByType(type);
            if (!string.IsNullOrWhiteSpace(catalogItem.Type) &&
                catalogItem.Size2D.x > 0f && catalogItem.Size2D.y > 0f)
            {
                return catalogItem.Size2D;
            }

            switch (type.Trim())
            {
                case "ArduinoUno": return new Vector2(260f, 240f);
                case "ArduinoNano": return new Vector2(240f, 200f);
                case "ArduinoProMini": return new Vector2(240f, 200f);
                case "Resistor": return new Vector2(140f, 50f);
                case "Capacitor": return new Vector2(120f, 50f);
                case "LED": return new Vector2(120f, 50f);
                case "DCMotor": return new Vector2(140f, 60f);
                case "Battery": return new Vector2(140f, 60f);
                case "TextNote": return new Vector2(200f, 80f);
                default: return new Vector2(DefaultComponentWidth, DefaultComponentHeight);
            }
        }
    }
}
