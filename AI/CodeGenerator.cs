namespace Playwright.AiFramework.AI;

/// <summary>
/// Generates or UPDATES Page Objects and Step Definitions after each AI test run.
///
/// Key behaviours:
///  • Page class name comes from the Feature title, not the scenario title
///    → All scenarios in "Login Page" feature share LoginPage.generated.cs
///  • Registry context is read before every generation call
///    → Claude knows what already exists and reuses/extends instead of duplicating
///  • Existing .generated.cs files are sent back to Claude as context
///    → Output is always the complete, merged class (old + new methods)
/// </summary>
public class CodeGenerator
{
    private readonly AnthropicClient _claude;
    private readonly RegistryReader  _registry;
    private readonly string          _projectRoot;

    public CodeGenerator(AnthropicClient claude)
    {
        _claude      = claude;
        _registry    = new RegistryReader();
        _projectRoot = FindProjectRoot();
    }

    public async Task GenerateArtifactsAsync(string scenarioText, string featureTitle)
    {
        // Fix 1: page class name derived from feature title passed by AiGenerationHook
        var pageClassName = DerivePageClassName(featureTitle);
        var registryCtx  = await _registry.BuildContextAsync();

        Console.WriteLine(string.IsNullOrEmpty(registryCtx)
            ? "  🔍 Registry: empty (first generation)"
            : "  🔍 Registry: existing code found — Claude will extend, not duplicate");

        // ── Pass 1: Page Object ───────────────────────────────────────────────
        await GenerateOrUpdatePageObjectAsync(scenarioText, pageClassName, registryCtx);

        // ── Pass 2: Step Definitions (uses methods extracted from Pass 1) ─────
        // Fix 2: read the JUST-WRITTEN page object, extract real method signatures
        var pageCode         = await _registry.ReadPageFileAsync(pageClassName);
        var availableMethods = pageCode is not null
            ? ExtractMethodSignatures(pageCode).ToList()
            : new List<string>();

        if (availableMethods.Any())
            Console.WriteLine($"  🔗 Extracted {availableMethods.Count} method(s) from {pageClassName}");
        else
            Console.WriteLine($"  ⚠️  No methods found in {pageClassName} — step definitions may be incomplete");

        await GenerateOrUpdateStepDefinitionsAsync(
            scenarioText, pageClassName, registryCtx, availableMethods);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 1 — Page Object
    // ─────────────────────────────────────────────────────────────────────────

    private async Task GenerateOrUpdatePageObjectAsync(
        string scenarioText, string pageClassName, string registryCtx)
    {
        var dir  = Path.Combine(_projectRoot, "Pages");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{pageClassName}.generated.cs");

        var existingCode = await _registry.ReadPageFileAsync(pageClassName);

        var sb = new System.Text.StringBuilder();
        if (existingCode is not null)
        {
            sb.AppendLine("Update this existing Page Object — add any methods needed for the new scenario.");
            sb.AppendLine("Keep ALL existing methods unchanged. Output the COMPLETE merged class.");
            sb.AppendLine();
            sb.AppendLine("EXISTING CLASS:");
            sb.AppendLine(existingCode);
            sb.AppendLine();
            sb.AppendLine("NEW SCENARIO TO SUPPORT:");
            sb.Append(scenarioText);
        }
        else
        {
            sb.AppendLine($"Page class name: {pageClassName}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(registryCtx))
            {
                sb.AppendLine(registryCtx);
                sb.AppendLine();
            }
            sb.AppendLine("Scenario:");
            sb.Append(scenarioText);
        }

        var label = existingCode is not null ? "Updating" : "Creating";
        Console.WriteLine($"  🏗️  {label} Page Object → {pageClassName}.generated.cs");

        var raw  = await _claude.GenerateAsync(SystemPrompts.PageObjectGenerator(registryCtx), sb.ToString());
        var code = ExtractCSharpCode(raw);   // Fix 3: robust extraction

        await File.WriteAllTextAsync(path, code);
        Console.WriteLine($"  📄 Pages/{pageClassName}.generated.cs");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 2 — Step Definitions
    // ─────────────────────────────────────────────────────────────────────────

    private async Task GenerateOrUpdateStepDefinitionsAsync(
        string scenarioText, string pageClassName, string registryCtx,
        List<string> availableMethods)                         // Fix 2: receive actual methods
    {
        var dir           = Path.Combine(_projectRoot, "StepDefinitions");
        Directory.CreateDirectory(dir);
        var stepClassName = pageClassName.Replace("Page", "Steps");
        var path          = Path.Combine(dir, $"{stepClassName}.generated.cs");

        var existingCode = await _registry.ReadStepFileAsync(stepClassName);

        var sb = new System.Text.StringBuilder();
        if (existingCode is not null)
        {
            sb.AppendLine("Update this existing step definitions class.");
            sb.AppendLine("Add bindings for any steps not already bound. Keep ALL existing bindings.");
            sb.AppendLine("Output the COMPLETE merged class.");
            sb.AppendLine();
            sb.AppendLine("EXISTING CLASS:");
            sb.AppendLine(existingCode);
            sb.AppendLine();
            sb.AppendLine("NEW SCENARIO STEPS TO ADD:");
            sb.Append(scenarioText);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(registryCtx))
            {
                sb.AppendLine(registryCtx);
                sb.AppendLine();
            }
            sb.AppendLine("Scenario:");
            sb.Append(scenarioText);
        }

        var label = existingCode is not null ? "Updating" : "Creating";
        Console.WriteLine($"  🏗️  {label} Step Definitions → {stepClassName}.generated.cs");

        // Fix 2: pass availableMethods to prompt so Claude cannot invent method names
        var raw  = await _claude.GenerateAsync(
            SystemPrompts.StepDefinitionGenerator(pageClassName, registryCtx, availableMethods),
            sb.ToString());
        var code = ExtractCSharpCode(raw);   // Fix 3: robust extraction

        await File.WriteAllTextAsync(path, code);
        Console.WriteLine($"  📄 StepDefinitions/{stepClassName}.generated.cs");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fix 3 — Robust C# code extraction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts pure C# code from a Claude response.
    ///
    /// Strategy 1 — strip markdown fences (```csharp ... ```)
    /// Strategy 2 — scan line-by-line for the first real C# token
    ///              (namespace, [Binding], public class)
    ///              This handles prompt text that spills into the response (Issue 3).
    /// </summary>
    private static string ExtractCSharpCode(string raw)
    {
        var s = raw.Trim();

        // ── Strategy 1: markdown fences ──────────────────────────────────────
        if (s.StartsWith("```"))
        {
            var fenceLines = s.Split('\n');
            var fenced = string.Join("\n",
                fenceLines.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```")));
            if (!string.IsNullOrWhiteSpace(fenced))
                return fenced.Trim();
        }

        // ── Strategy 2: find first C# token (handles prompt spill) ───────────
        var allLines = s.Split('\n');
        for (var i = 0; i < allLines.Length; i++)
        {
            var t = allLines[i].Trim();
            if (t.StartsWith("namespace ")   ||
                t.StartsWith("[Binding]")     ||
                t.StartsWith("public class ") ||
                t.StartsWith("// <auto-generated"))
            {
                return string.Join("\n", allLines.Skip(i)).Trim();
            }
        }

        // ── Fallback: return as-is and let the compiler surface any errors ────
        return s;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fix 2: extract all "public async Task MethodName(params)" from a .cs file.
    /// These are passed to the step definition prompt as the only callable methods.
    /// </summary>
    private static IEnumerable<string> ExtractMethodSignatures(string code) =>
        System.Text.RegularExpressions.Regex
            .Matches(code, @"public\s+async\s+Task\s+(\w+\s*\([^)]*\))")
            .Select(m => $"public async Task {m.Groups[1].Value.Trim()}");

    /// <summary>
    /// Fix 1: derive page class name from the Feature title.
    ///
    /// "Login Page"           → LoginPage
    /// "Checkboxes Page"      → CheckboxesPage
    /// "Add Remove Elements"  → AddRemoveElementsPage
    /// </summary>
    private static string DerivePageClassName(string featureTitle)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(
            featureTitle,
            @"\b(Feature|Tests?|Specs?)\b", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        var pascal = string.Concat(
            System.Text.RegularExpressions.Regex
                .Replace(clean, @"[^a-zA-Z0-9\s]", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpper(w[0]) + w[1..]));

        // Guarantee exactly one "Page" suffix, no double "PagePage"
        return pascal.EndsWith("Page", StringComparison.OrdinalIgnoreCase)
            ? pascal
            : pascal + "Page";
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}