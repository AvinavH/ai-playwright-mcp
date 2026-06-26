namespace Playwright.AiFramework.AI;

/// <summary>
/// Persists generated Playwright action plans to disk as JSON.
///
/// Demo value:
///   • First run  → calls Claude API, saves JSON to GeneratedActions/
///   • Next runs  → loads from disk (fast, no API cost)
///   • Show the .json file in the interview to prove AI generated the steps
///   • Delete the file to force re-generation
/// </summary>
public class ActionCache
{
    private readonly string _cacheDir;

    public ActionCache()
    {
        // Place cache files in GeneratedActions/ at the project root
        // (not in the build output) so they survive clean builds.
        var root = FindProjectRoot();
        _cacheDir = Path.Combine(root, "GeneratedActions");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Returns cached actions or null if no cache file exists.</summary>
    public async Task<List<PlaywrightAction>?> TryGetAsync(string scenarioTitle)
    {
        var path = BuildPath(scenarioTitle);
        if (!File.Exists(path)) return null;

        Console.WriteLine($"  📁 Cache hit  →  {Path.GetFileName(path)}");
        var json = await File.ReadAllTextAsync(path);
        return JsonConvert.DeserializeObject<List<PlaywrightAction>>(json);
    }

    /// <summary>Saves the action list to a JSON file named after the scenario.</summary>
    public async Task SaveAsync(string scenarioTitle, List<PlaywrightAction> actions)
    {
        var path = BuildPath(scenarioTitle);
        await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(actions, Formatting.Indented));
        Console.WriteLine($"  💾 Saved      →  {path}");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private string BuildPath(string scenarioTitle)
    {
        var invalid  = Path.GetInvalidFileNameChars().Concat(new[] { ' ' }).ToHashSet();
        var safeName = new string(scenarioTitle.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(_cacheDir, $"{safeName}.json");
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
