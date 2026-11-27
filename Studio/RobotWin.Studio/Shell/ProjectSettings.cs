using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [workspaces] A project's app-owned settings (the package's settings.json, opaque to the core): one JSON object whose
    /// entries each belong to a feature (the project's own layout is "workspace"). Setting one keeps the others, and a file
    /// that cannot be read counts as empty rather than failing the project.
    /// </summary>
    public static class ProjectSettings
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        private static JsonObject Read(string? settings)
        {
            if (string.IsNullOrWhiteSpace(settings)) return new JsonObject();
            try
            {
                return JsonNode.Parse(settings!) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                return new JsonObject();
            }
        }

        /// <summary>One entry, or null when it is not there (or the settings cannot be read).</summary>
        public static JsonNode? Get(string? settings, string key) => Read(settings)[key]?.DeepClone();

        /// <summary>The settings with one entry set (null takes it out); null when nothing is left.</summary>
        public static string? With(string? settings, string key, JsonNode? value)
        {
            var o = Read(settings);
            if (value == null) o.Remove(key);
            else o[key] = value;
            return o.Count == 0 ? null : o.ToJsonString(Json);
        }
    }

    /// <summary>[workspaces] A built-in workspace's tool strips, written "id=open|folded|closed,…".</summary>
    public static class WorkspaceStrips
    {
        /// <summary>Each strip as (id, shown, folded): "open" shown and open, "folded" shown as its handle, "closed" hidden; others are skipped.</summary>
        public static IEnumerable<(string Id, bool Shown, bool Folded)> Parse(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) yield break;
            foreach (var entry in spec!.Split(','))
            {
                var kv = entry.Split('=');
                if (kv.Length != 2 || kv[0].Trim().Length == 0) continue;
                string id = kv[0].Trim();
                switch (kv[1].Trim())
                {
                    case "open": yield return (id, true, false); break;
                    case "folded": yield return (id, true, true); break;
                    case "closed": yield return (id, false, false); break;
                }
            }
        }
    }
}
