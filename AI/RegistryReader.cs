namespace Playwright.AiFramework.AI;

/// <summary>
/// Scans Pages/ and StepDefinitions/ and returns a formatted summary of what
/// already exists. Included in every generation prompt so Claude knows what
/// to reuse and what not to duplicate.
/// </summary>
public class RegistryReader
{
    private readonly string _projectRoot;

    public RegistryReader() => _projectRoot = FindProjectRoot();

    // ── Full context summary ──────────────────────────────────────────────────

    public async Task<string> BuildContextAsync()
    {
        var pages = await ReadPagesAsync();
        var steps = await ReadStepDefinitionsAsync();

        if (string.IsNullOrWhiteSpace(pages) && string.IsNullOrWhiteSpace(steps))
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## EXISTING CODE — reuse methods/steps below; do NOT duplicate them");

        if (!string.IsNullOrWhiteSpace(pages))
        {
            sb.AppendLine("\n### Page Objects:");
            sb.Append(pages);
        }

        if (!string.IsNullOrWhiteSpace(steps))
        {
            sb.AppendLine("\n### Step Definitions:");
            sb.Append(steps);
        }

        return sb.ToString();
    }

    // ── Individual file readers ───────────────────────────────────────────────

    public async Task<string?> ReadPageFileAsync(string pageClassName)
    {
        var path = Path.Combine(_projectRoot, "Pages", $"{pageClassName}.generated.cs");
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    public async Task<string?> ReadStepFileAsync(string stepClassName)
    {
        var path = Path.Combine(_projectRoot, "StepDefinitions", $"{stepClassName}.generated.cs");
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    // ── Internal scanners ─────────────────────────────────────────────────────

    private async Task<string> ReadPagesAsync()
    {
        var dir = Path.Combine(_projectRoot, "Pages");
        if (!Directory.Exists(dir)) return string.Empty;

        var sb = new System.Text.StringBuilder();

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var content   = await File.ReadAllTextAsync(file);
            var className = ExtractClassName(content);
            var methods   = ExtractPublicMethods(content);

            if (className is null) continue;

            sb.AppendLine($"  Class: {className}  →  Pages/{Path.GetFileName(file)}");
            foreach (var m in methods)
                sb.AppendLine($"    + {m}");
        }

        return sb.ToString();
    }

    private async Task<string> ReadStepDefinitionsAsync()
    {
        var dir = Path.Combine(_projectRoot, "StepDefinitions");
        if (!Directory.Exists(dir)) return string.Empty;

        var sb = new System.Text.StringBuilder();

        foreach (var file in Directory.GetFiles(dir, "*.generated.cs"))
        {
            var content  = await File.ReadAllTextAsync(file);
            var bindings = ExtractStepBindings(content);
            if (!bindings.Any()) continue;

            sb.AppendLine($"  File: {Path.GetFileName(file)}");
            foreach (var b in bindings)
                sb.AppendLine($"    • {b}");
        }

        return sb.ToString();
    }

    // ── Parsers ───────────────────────────────────────────────────────────────

    private static string? ExtractClassName(string code)
    {
        var m = Regex.Match(code, @"public\s+class\s+(\w+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static IEnumerable<string> ExtractPublicMethods(string code) =>
        Regex.Matches(code, @"public\s+async\s+Task\s+(\w+\([^)]*\))")
             .Select(m => m.Groups[1].Value);

    private static IEnumerable<string> ExtractStepBindings(string code) =>
        Regex.Matches(code, @"\[(Given|When|Then)\(@""([^""]+)""\)\]")
             .Select(m => $"[{m.Groups[1].Value}] \"{m.Groups[2].Value}\"");

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}