using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace RobotWin.Studio.Firmware
{
    public static partial class ArduinoCompiler
    {
        private static readonly ConcurrentDictionary<string, string> ToolchainCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// [toolchain] What a build for <paramref name="fqbn"/> is made with, as a project keeps it: "arduino-cli 1.6.0 ·
        /// arduino:avr 1.8.8" (the compiler and the board core it installed, which fix avr-gcc and the Arduino core
        /// libraries). Asked from arduino-cli once per session and target; null when there is no compiler to ask.
        /// </summary>
        public static async Task<string?> ToolchainOfAsync(string fqbn)
        {
            if (IsEsp32(fqbn)) return null;   // the fast mode builds against the app's own HAL; its version is the app's
            string? cli = FindCli();
            if (cli == null) return null;
            string platform = string.Join(":", fqbn.Split(':').Take(2));
            string key = cli + "|" + platform;
            if (ToolchainCache.TryGetValue(key, out var known)) return known;
            var (okVersion, version) = await RunCliAsync("version", "--format", "json").ConfigureAwait(false);
            var (okCores, cores) = await RunCliAsync("core", "list", "--format", "json").ConfigureAwait(false);
            string id = ToolchainInfo.Describe(okVersion ? ToolchainInfo.CliVersion(version) : null, platform, okCores ? ToolchainInfo.CoreVersion(cores, platform) : null);
            ToolchainCache[key] = id;
            return id;
        }
    }
}
