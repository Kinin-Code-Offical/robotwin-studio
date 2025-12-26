using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Specs;

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
            return JsonSerializer.Deserialize<T>(json, _options); // Reverted 'Options' to '_options' to maintain consistency with existing private field
        }

        // Persistence
        public static void SaveProject(ProjectManifest manifest, string filePath)
        {
            var json = Serialize(manifest);
            File.WriteAllText(filePath, json);
        }

        public static ProjectManifest? LoadProject(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var json = File.ReadAllText(filePath);
            return Deserialize<ProjectManifest>(json);
        }
    }
}
