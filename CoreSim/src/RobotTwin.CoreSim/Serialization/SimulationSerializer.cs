using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Serialization
{
    /// <summary>
    /// Centralized JSON serialization logic to ensure determinism and compatibility.
    /// </summary>
    public static class SimulationSerializer
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _options);
        }

        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _options);
        }
    }
}
