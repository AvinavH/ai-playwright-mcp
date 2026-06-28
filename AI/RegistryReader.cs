namespace Playwright.AiFramework.AI;

/// <summary>
/// Scans Pages/ and StepDefinitions/ and builds a structured summary of what
/// already exists. Included in every generation prompt so Claude knows what
/// to reuse and what not to duplicate.
///
/// File resolution order (both methods):
///   1. {name}.cs          — user has graduated (renamed) the file; owns it now
///   2. {name}.generated.cs — still AI-managed
/// This means CodeGenerator writes back to the same file the dev is working in,
/// preventing duplicate-class errors when a new @ai_generated scenario is added
/// to a feature whose page/step files have already been graduated.
/// </summary>
public class RegistryReader
{
    private readonly string _projectRoot;

    public RegistryReader() => _projectRoot = FindProjectRoot();

    // ── Context summary ───────────────────────────────────────────────────────

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

    // ── File readers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the content of the page file if it exists (either graduated
    /// .cs or AI-managed .generated.cs).
    /// </summary>
    public async Task<string?> ReadPageFileAsync(string pageClassName)
    {
        var path = FindPageFilePath(pageClassName);
        return path is not null ? await File.ReadAllTextAsync(path) : null;
    }

    /// <summary>
    /// Returns the content of the step definitions file if it exists.
    /// </summary>
    public async Task<string?> ReadStepFileAsync(string stepClassName)
    {
        var path = FindStepFilePath(stepClassName);
        return path is not null ? await File.ReadAllTextAsync(path) : null;
    }

    /// <summary>
    /// Returns the path of the existing page file, preferring the graduated
    /// .cs over the .generated.cs. Returns null if neither exists.
    /// CodeGenerator uses this to write updates back to the correct file.
    /// </summary>
    public string? FindPageFilePath(string pageClassName)
    {
        var dir = Path.Combine(_projectRoot, "Pages");
        return new[]
        {
            Path.Combine(dir, $"{pageClassName}.cs"),          // graduated
            Path.Combine(dir, $"{pageClassName}.generated.cs") // AI-managed
        }
        .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Returns the path of the existing step definitions file.
    /// </summary>
    public string? FindStepFilePath(string stepClassName)
    {
        var dir = Path.Combine(_projectRoot, "StepDefinitions");
        return new[]
        {
            Path.Combine(dir, $"{stepClassName}.cs"),
            Path.Combine(dir, $"{stepClassName}.generated.cs")
        }
        .FirstOrDefault(File.Exists);
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

        // Scan both .generated.cs and .cs files
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
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
        var m = System.Text.RegularExpressions.Regex.Match(code, @"public\s+class\s+(\w+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static IEnumerable<string> ExtractPublicMethods(string code) =>
        System.Text.RegularExpressions.Regex
            .Matches(code, @"public\s+async\s+Task\s+(\w+\([^)]*\))")
            .Select(m => $"public async Task {m.Groups[1].Value}");

    private static IEnumerable<string> ExtractStepBindings(string code) =>
        System.Text.RegularExpressions.Regex
            .Matches(code, @"\[(Given|When|Then)\(@""([^""]+)""\)\]")
            .Select(m => $"[{m.Groups[1].Value}] \"{m.Groups[2].Value}\"");

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}