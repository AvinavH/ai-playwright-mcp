namespace Playwright.AiFramework.AI;

/// <summary>
/// Reads .feature files from disk and extracts a single scenario block by title.
/// This text is sent verbatim to Claude as the user message.
/// </summary>
public class FeatureFileReader
{
    private readonly string _featuresDir;

    public FeatureFileReader()
    {
        // Look for Features/ in the project root first, then fall back to the
        // build output directory (covers CI scenarios where files are copied).
        var root = FindProjectRoot();

        var candidates = new[]
        {
            Path.Combine(root, "Features"),
            Path.Combine(AppContext.BaseDirectory, "Features")
        };

        _featuresDir = candidates.FirstOrDefault(Directory.Exists)
                       ?? throw new DirectoryNotFoundException(
                           $"Features directory not found. Searched:\n  " +
                           string.Join("\n  ", candidates));
    }

    /// <summary>
    /// Finds the scenario with the given title across all .feature files
    /// and returns the Feature + Scenario + Steps text block.
    /// </summary>
    public async Task<string> GetScenarioTextAsync(string scenarioTitle)
    {
        foreach (var file in Directory.GetFiles(_featuresDir, "*.feature", SearchOption.AllDirectories))
        {
            var content  = await File.ReadAllTextAsync(file);
            var scenario = ExtractScenario(content, scenarioTitle);
            if (scenario is not null)
                return scenario;
        }

        throw new InvalidOperationException(
            $"Scenario '{scenarioTitle}' not found in any .feature file under {_featuresDir}");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string? ExtractScenario(string featureContent, string targetTitle)
    {
        var lines       = featureContent.Split('\n');
        var result      = new List<string>();
        var featureLine = string.Empty;
        var inScenario  = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Capture the Feature: line for context (Claude uses it)
            if (trimmed.StartsWith("Feature:"))
            {
                featureLine = trimmed;
                continue;
            }

            if (!inScenario)
            {
                // Look for the target scenario
                if (trimmed.StartsWith("Scenario:") &&
                    trimmed.Contains(targetTitle, StringComparison.OrdinalIgnoreCase))
                {
                    inScenario = true;
                    if (!string.IsNullOrEmpty(featureLine))
                        result.Add(featureLine);
                    result.Add(trimmed);
                }
                // Tags, blank lines, other scenarios → keep scanning
                continue;
            }

            // ── We're inside the target scenario ──────────────────────────────
            // Stop at the next scenario boundary (its @tag line or Scenario: keyword)
            if (trimmed.StartsWith("@") || trimmed.StartsWith("Scenario:"))
                break;

            if (!string.IsNullOrWhiteSpace(trimmed))
                result.Add(trimmed);
        }

        return inScenario ? string.Join("\n", result) : null;
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
