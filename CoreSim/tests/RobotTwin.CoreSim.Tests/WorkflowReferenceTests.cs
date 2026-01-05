using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Tests;

public class WorkflowReferenceTests
{
    [Fact]
    public void CiWorkflowDoesNotReferenceMissingRepoFiles()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        Assert.True(File.Exists(workflowPath), $"Workflow not found: {workflowPath}");

        var workflowText = File.ReadAllText(workflowPath);

        // Capture repo-relative paths like ./tools/scripts/foo.ps1
        var matches = Regex.Matches(workflowText, @"\./[A-Za-z0-9_\-./]+", RegexOptions.Compiled);

        var checkedAny = false;

        foreach (Match match in matches)
        {
            var raw = match.Value;

            // Ignore action refs like ./ isn't used there; still be defensive.
            if (!raw.StartsWith("./", StringComparison.Ordinal))
            {
                continue;
            }

            // Only validate likely repo files (scripts/config), not directories.
            if (!(raw.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                  || raw.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                  || raw.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                  || raw.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                  || raw.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                  || raw.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var relativePath = raw[2..].Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));

            // Only enforce paths that stay within repo root.
            if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            checkedAny = true;
            Assert.True(File.Exists(fullPath), $"CI workflow references missing file: {raw} -> {fullPath}");
        }

        Assert.True(checkedAny, "No repo file references found in ci.yml to validate.");
    }

    [Fact]
    public void PullRequestTemplateDoesNotReferenceRemovedAntigravityPaths()
    {
        var repoRoot = FindRepoRoot();
        var templatePath = Path.Combine(repoRoot, ".github", "pull_request_template.md");
        Assert.True(File.Exists(templatePath), $"PR template not found: {templatePath}");

        var text = File.ReadAllText(templatePath);
        Assert.DoesNotContain("docs/antigravity", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root (global.json not found).");
    }
}
