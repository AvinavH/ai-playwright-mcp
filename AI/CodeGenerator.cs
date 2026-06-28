namespace Playwright.AiFramework.AI;
/// <summary>
///  Generates or UPDATES Page Objects and Step Definitions after each AI test run.
///
///  Key behaviours:
///  • Page class name comes from the Feature title, not the scenario title
///    → All scenarios in "Login Page" feature share LoginPage.generated.cs
///  • Registry context is read before every generation call
///    → Claude knows what already exists and reuses/extends instead of duplicating
///  • Existing .generated.cs files are sent back to Claude as context
///    → Output is always the complete, merged class (old + new methods)
///
///   File resolution strategy (handles the graduation workflow):
///   When a dev renames LoginPage.generated.cs → LoginPage.cs and removes
///   @ai_generated from the feature level, then adds a NEW @ai_generated scenario:
///
///   FindPageFilePath checks .cs first, then .generated.cs.
///   CodeGenerator writes the update back to whichever file was found.
///   No duplicate file is ever created.
///
///   Outcome matrix:
///   ┌─────────────────────────┬──────────────────────────────────────────┐
///   │ File state              │ CodeGenerator behaviour                  │
///   ├─────────────────────────┼──────────────────────────────────────────┤
///   │ Neither exists          │ Create LoginPage.generated.cs            │
///   │ .generated.cs only      │ Update LoginPage.generated.cs            │
///   │ .cs only (graduated)    │ Update LoginPage.cs (dev's file)         │
///   │ Both (should not happen)│ Update .cs (it's found first)            │
///   └─────────────────────────┴──────────────────────────────────────────┘
///   Locator accuracy fix:
///   GenerateArtifactsAsync now accepts the verified action plan and passes it
///   to PageObjectGenerator as additional context. The action plan was produced
///   by the site-aware TestActionGenerator prompt AND proven correct by executing
///   in a real browser, so its locators are the ground truth Claude should use
///   when writing page methods — not guesses derived from method names alone.
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
    /// <summary>
    /// <param name="verifiedActions">
    /// The action plan that was proven correct in the browser.
    /// Passed through to PageObjectGenerator as locator ground truth.
    /// </param>
    /// </summary>
    public async Task GenerateArtifactsAsync(
        string scenarioText,
        string featureTitle,
        IReadOnlyList<PlaywrightAction>? verifiedActions = null)
    {
        var pageClassName = DerivePageClassName(featureTitle);
        var registryCtx  = await _registry.BuildContextAsync();

        Console.WriteLine(string.IsNullOrEmpty(registryCtx)
            ? "  🔍 Registry: empty (first generation)"
            : "  🔍 Registry: existing code found — Claude will extend, not duplicate");

        await GenerateOrUpdatePageObjectAsync(
            scenarioText, pageClassName, registryCtx, verifiedActions);

        var pageCode         = await _registry.ReadPageFileAsync(pageClassName);
        var availableMethods = pageCode is not null
            ? ExtractMethodSignatures(pageCode).ToList()
            : new List<string>();

        if (availableMethods.Any())
            Console.WriteLine($"  🔗 {availableMethods.Count} method(s) available in {pageClassName}");

        await GenerateOrUpdateStepDefinitionsAsync(
            scenarioText, pageClassName, registryCtx, availableMethods);
    }

    // ── Pass 1: Page Object ───────────────────────────────────────────────────
    private async Task GenerateOrUpdatePageObjectAsync(
        string scenarioText,
        string pageClassName,
        string registryCtx,
        IReadOnlyList<PlaywrightAction>? verifiedActions)
    {
        Directory.CreateDirectory(Path.Combine(_projectRoot, "Pages"));

        var existingPath = _registry.FindPageFilePath(pageClassName);
        var outputPath   = existingPath
                           ?? Path.Combine(_projectRoot, "Pages", $"{pageClassName}.generated.cs");
        var existingCode = existingPath is not null
                           ? await File.ReadAllTextAsync(existingPath)
                           : null;

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

        var label = existingCode is not null
            ? $"Updating {Path.GetFileName(outputPath)}"
            : $"Creating {pageClassName}.generated.cs";
        Console.WriteLine($"  🏗️  {label}");

        var raw  = await _claude.GenerateAsync(
            SystemPrompts.PageObjectGenerator(registryCtx, verifiedActions),
            sb.ToString());
        var code = ExtractCSharpCode(raw);

        await File.WriteAllTextAsync(outputPath, code);
        Console.WriteLine($"  📄 {Path.GetRelativePath(_projectRoot, outputPath)}");
    }

    // ── Pass 2: Step Definitions ──────────────────────────────────────────────

    private async Task GenerateOrUpdateStepDefinitionsAsync(
        string scenarioText,
        string pageClassName,
        string registryCtx,
        List<string> availableMethods)
    {
        Directory.CreateDirectory(Path.Combine(_projectRoot, "StepDefinitions"));

        var stepClassName = pageClassName.Replace("Page", "Steps");
        var existingPath  = _registry.FindStepFilePath(stepClassName);
        var outputPath    = existingPath
                            ?? Path.Combine(_projectRoot, "StepDefinitions", $"{stepClassName}.generated.cs");
        var existingCode  = existingPath is not null
                            ? await File.ReadAllTextAsync(existingPath)
                            : null;

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

        var label = existingCode is not null
            ? $"Updating {Path.GetFileName(outputPath)}"
            : $"Creating {stepClassName}.generated.cs";
        Console.WriteLine($"  🏗️  {label}");

        var raw  = await _claude.GenerateAsync(
            SystemPrompts.StepDefinitionGenerator(pageClassName, registryCtx, availableMethods),
            sb.ToString());

        // EnsurePagesImport guarantees the using statement is present regardless
        // of whether Claude included it — prompt instructions for imports are
        // unreliable; deterministic post-processing is not.
        var code = EnsurePagesImport(ExtractCSharpCode(raw));

        await File.WriteAllTextAsync(outputPath, code);
        Console.WriteLine($"  📄 {Path.GetRelativePath(_projectRoot, outputPath)}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Guarantees "using Playwright.AiFramework.Pages;" is present in every
    /// generated step definitions file, regardless of what Claude returned.
    /// Claude frequently ignores prompt instructions for using statements;
    /// post-processing here is the reliable alternative.
    /// </summary>
    private static string EnsurePagesImport(string code)
    {
        const string required = "using Playwright.AiFramework.Pages;";

        if (code.Contains(required))
            return code;

        // Insert at the right position:
        //   Before the first non-using line (namespace / [Binding] / public class)
        //   so existing using statements stay grouped together.
        var lines         = code.Split('\n').ToList();
        var firstNonUsing = lines.FindIndex(
            l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("using "));

        if (firstNonUsing > 0)
        {
            lines.Insert(firstNonUsing, string.Empty);
            lines.Insert(firstNonUsing, required);
        }
        else
        {
            lines.Insert(0, string.Empty);
            lines.Insert(0, required);
        }

        return string.Join("\n", lines);
    }

    private static string ExtractCSharpCode(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var fenced = string.Join("\n",
                s.Split('\n').Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```")));
            if (!string.IsNullOrWhiteSpace(fenced))
                return fenced.Trim();
        }

        var lines = s.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("namespace ") || t.StartsWith("[Binding]") || t.StartsWith("public class "))
                return string.Join("\n", lines.Skip(i)).Trim();
        }

        return s;
    }

    private static IEnumerable<string> ExtractMethodSignatures(string code) =>
        System.Text.RegularExpressions.Regex
            .Matches(code, @"public\s+async\s+Task\s+(\w+\s*\([^)]*\))")
            .Select(m => $"public async Task {m.Groups[1].Value.Trim()}");

    private static string DerivePageClassName(string featureTitle)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(
            featureTitle, @"\b(Feature|Tests?|Specs?)\b", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        var pascal = string.Concat(
            System.Text.RegularExpressions.Regex
                .Replace(clean, @"[^a-zA-Z0-9\s]", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpper(w[0]) + w[1..]));

        return pascal.EndsWith("Page", StringComparison.OrdinalIgnoreCase)
            ? pascal : pascal + "Page";
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}