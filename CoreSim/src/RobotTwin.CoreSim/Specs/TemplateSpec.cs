using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Specs
{
    /// <summary>
    /// Defines a Project Template, which is a pre-configured system archetype.
    /// Used to initialize a new Session.
    /// </summary>
    [JsonConverter(typeof(TemplateSpecJsonConverter))]
    public class TemplateSpec
    {
        public required string TemplateId { get; set; }
        
        // Backward compatibility for RobotWin / Tests
        public string ID { get => TemplateId; set => TemplateId = value; }

        public required string DisplayName { get; set; }
        
        // Backward compatibility for RobotWin / Tests
        public string Name { get => DisplayName; set => DisplayName = value; }

        public required string Description { get; set; }

        /// <summary>
        /// The type of system: "CircuitOnly", "Robot", "Mechatronic", etc.
        /// </summary>
        public required string SystemType { get; set; }

        /// <summary>
        /// Default Circuit Specification (JSON or ID).
        /// </summary>
        public string? DefaultCircuitId { get; set; }

        /// <summary>
        /// Default Robot Specification (JSON or ID).
        /// </summary>
        public string? DefaultRobotId { get; set; }

        /// <summary>
        /// Default World environment ID.
        /// </summary>
        public string? DefaultWorldId { get; set; }

        // Backward compatibility / Embedded specs
        public CircuitSpec? DefaultCircuit { get; set; }
        public RobotSpec? DefaultRobot { get; set; }
        public WorldSpec? DefaultWorld { get; set; }
    }

    internal sealed class TemplateSpecJsonConverter : JsonConverter<TemplateSpec>
    {
        public override TemplateSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("TemplateSpec must be a JSON object.");
            }

            string? templateId = null;
            string? displayName = null;
            string? description = null;
            string? systemType = null;
            string? defaultCircuitId = null;
            string? defaultRobotId = null;
            string? defaultWorldId = null;
            CircuitSpec? defaultCircuit = null;
            RobotSpec? defaultRobot = null;
            WorldSpec? defaultWorld = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("TemplateSpec property name expected.");
                }

                string propertyName = reader.GetString() ?? string.Empty;
                string key = propertyName.Trim().ToLowerInvariant();
                if (!reader.Read())
                {
                    throw new JsonException("TemplateSpec property value missing.");
                }

                switch (key)
                {
                    case "templateid":
                    case "id":
                        templateId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "displayname":
                    case "name":
                        displayName = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "description":
                        description = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "systemtype":
                        systemType = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "defaultcircuitid":
                        defaultCircuitId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "defaultrobotid":
                        defaultRobotId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "defaultworldid":
                        defaultWorldId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                        break;
                    case "defaultcircuit":
                        defaultCircuit = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<CircuitSpec>(ref reader, options);
                        break;
                    case "defaultrobot":
                        defaultRobot = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<RobotSpec>(ref reader, options);
                        break;
                    case "defaultworld":
                        defaultWorld = reader.TokenType == JsonTokenType.Null
                            ? null
                            : JsonSerializer.Deserialize<WorldSpec>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new JsonException("TemplateSpec missing TemplateId (or legacy ID).");
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new JsonException("TemplateSpec missing DisplayName (or legacy Name).");
            }
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new JsonException("TemplateSpec missing Description.");
            }
            if (string.IsNullOrWhiteSpace(systemType))
            {
                throw new JsonException("TemplateSpec missing SystemType.");
            }

            return new TemplateSpec
            {
                TemplateId = templateId,
                DisplayName = displayName,
                Description = description,
                SystemType = systemType,
                DefaultCircuitId = defaultCircuitId,
                DefaultRobotId = defaultRobotId,
                DefaultWorldId = defaultWorldId,
                DefaultCircuit = defaultCircuit,
                DefaultRobot = defaultRobot,
                DefaultWorld = defaultWorld
            };
        }

        public override void Write(Utf8JsonWriter writer, TemplateSpec value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("TemplateId", value.TemplateId);
            writer.WriteString("DisplayName", value.DisplayName);
            writer.WriteString("Description", value.Description);
            writer.WriteString("SystemType", value.SystemType);

            if (!string.IsNullOrWhiteSpace(value.DefaultCircuitId))
            {
                writer.WriteString("DefaultCircuitId", value.DefaultCircuitId);
            }
            if (!string.IsNullOrWhiteSpace(value.DefaultRobotId))
            {
                writer.WriteString("DefaultRobotId", value.DefaultRobotId);
            }
            if (!string.IsNullOrWhiteSpace(value.DefaultWorldId))
            {
                writer.WriteString("DefaultWorldId", value.DefaultWorldId);
            }
            if (value.DefaultCircuit != null)
            {
                writer.WritePropertyName("DefaultCircuit");
                JsonSerializer.Serialize(writer, value.DefaultCircuit, options);
            }
            if (value.DefaultRobot != null)
            {
                writer.WritePropertyName("DefaultRobot");
                JsonSerializer.Serialize(writer, value.DefaultRobot, options);
            }
            if (value.DefaultWorld != null)
            {
                writer.WritePropertyName("DefaultWorld");
                JsonSerializer.Serialize(writer, value.DefaultWorld, options);
            }

            writer.WriteEndObject();
        }
    }
}

