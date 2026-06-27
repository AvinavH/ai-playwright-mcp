using Playwright.AiFramework.Reporting;

namespace Playwright.AiFramework.Hooks;

/// <summary>
/// Orchestrates AI test generation for every @ai_generated scenario.
/// Flow:
///   1. Read the Gherkin scenario text from the .feature file on disk
///   2. Check the disk cache (GeneratedActions/) — skip API call if already generated
///   3. If no cache → call Claude API with the scenario + system prompt
///   4. Parse the returned JSON into List;PlaywrightAction;
///   5. Persist to cache for future runs
///   6. Execute the actions via PlaywrightRunner (real browser interactions)
///
/// By the time Reqnroll starts executing individual step methods, all browser work
/// is already done. The AiStepDefinitions class then acts as a no-op reporter.
/// 
/// Retry strategy:
///   On failure, the failing action plan + the Playwright error message are sent
///   back to Claude. Claude analyses the error and returns a corrected plan.
///   This repeats up to MaxRetries times before the scenario is marked as failed.
///
/// Artifact guarantee:
///   Page Object and Step Definitions are ALWAYS written to disk — even when all
///   retries are exhausted — so the developer can review and fix them manually.
/// 
/// Allure integration:
///   • Initial action plan attached as JSON to every test case
///   • Each retry attempt is a named Allure step with the healed plan attached
///   • Generated Page Object and Step Definitions attached after code generation
///   • On failure, the error message is attached as a text note
/// </summary>
[Binding]
public class AiGenerationHook
{
    private const int MaxRetries = 2;

    private readonly PlaywrightContext _playwright;
    private readonly ScenarioContext   _scenario;
    private readonly FeatureContext    _feature;
    private readonly AnthropicClient   _claude;
    private readonly ActionCache       _cache;
    private readonly FeatureFileReader _featureReader;

    public AiGenerationHook(
        PlaywrightContext playwright,
        ScenarioContext   scenario,
        FeatureContext    feature)
    {
        _playwright    = playwright;
        _scenario      = scenario;
        _feature       = feature;
        _featureReader = new FeatureFileReader();
        _cache         = new ActionCache();

        var apiKey = AppConfig.Get("Anthropic:ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_API_KEY_HERE")
            throw new InvalidOperationException(
                "Set 'Anthropic:ApiKey' in appsettings.json " +
                "or via environment variable ANTHROPIC__APIKEY");

        _claude = new AnthropicClient(apiKey);
    }

    // ─────────────────────────────────────────────────────────────────────────

    [BeforeScenario("ai_generated", Order = 10)]
    public async Task GenerateAndRunAsync()
    {
        var title        = _scenario.ScenarioInfo.Title;
        var featureTitle = _feature.FeatureInfo.Title;

        PrintBanner(title);

        var scenarioText = await _featureReader.GetScenarioTextAsync(title);

        // ── 1. Get initial action plan ────────────────────────────────────────
        var actions = await _cache.TryGetAsync(title)
                      ?? await GenerateInitialActionsAsync(scenarioText, title);

        // Attach the action plan to the Allure report immediately
        AllureReporter.AttachActionPlan(actions, "AI Action Plan (initial)");

        // ── 2. Execute with self-healing retry ────────────────────────────────
        Exception? testFailure = null;
        try
        {
            await ExecuteWithRetryAsync(actions, scenarioText, title);
            _scenario["AiActionsExecuted"] = true;
            Console.WriteLine("\n  🎉 All actions completed\n");
        }
        catch (Exception ex)
        {
            testFailure = ex;
            Console.WriteLine($"\n  ❌ Test failed after {MaxRetries} retry attempt(s)");
            Console.WriteLine($"  Last error: {FirstLine(ex.Message)}");
            AllureReporter.AttachText(ex.Message, "Final Failure Details");
        }

        // ── 3. Always save and attach code artefacts ──────────────────────────
        Console.WriteLine("\n  📦 Saving code artefacts...");
        try
        {
            var codeGen = new CodeGenerator(_claude);
            await codeGen.GenerateArtifactsAsync(scenarioText, featureTitle);

            // Attach generated files to the Allure report so reviewers can
            // inspect the AI-generated code directly from the report UI
            AttachGeneratedCode(featureTitle);
        }
        catch (Exception codeEx)
        {
            Console.WriteLine($"  ⚠️  Code generation error (non-fatal): {codeEx.Message}");
        }

        // ── 4. Re-throw after artefacts are written ───────────────────────────
        if (testFailure is not null)
        {
            Console.WriteLine("  💾 Artefacts saved for manual review\n");
            throw testFailure;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Initial generation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<PlaywrightAction>> GenerateInitialActionsAsync(
        string scenarioText, string cacheKey)
    {
        Console.WriteLine("  🧠 Generating Playwright actions via Claude...\n");
        PrintScenarioBlock(scenarioText);

        var raw     = await _claude.GenerateAsync(SystemPrompts.TestActionGenerator, scenarioText);
        var actions = ParseActions(raw, cacheKey);

        Console.WriteLine($"  ✅ Generated {actions.Count} action(s)\n");
        PrintActionPlan(actions);
        await _cache.SaveAsync(cacheKey, actions);
        return actions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Retry loop
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ExecuteWithRetryAsync(
        List<PlaywrightAction> initialActions,
        string scenarioText,
        string cacheKey)
    {
        var actions   = initialActions;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            if (attempt > 1)
            {
                // Each retry is a named Allure step so the report clearly shows
                // how many self-heal iterations were needed
                await AllureReporter.StepAsync(
                    $"🔄 Self-heal retry {attempt - 1}/{MaxRetries}",
                    async () =>
                    {
                        await ResetPageAsync();
                        actions = await HealActionsAsync(actions, scenarioText, lastError!);
                        await _cache.SaveAsync(cacheKey, actions);

                        // Attach the healed plan so each retry's attempt is traceable
                        AllureReporter.AttachActionPlan(
                            actions, $"Healed Action Plan (retry {attempt - 1})");
                    });
            }

            Console.WriteLine(
                $"\n  ▶  Executing {actions.Count} action(s) — attempt {attempt}/{MaxRetries + 1}:\n");

            try
            {
                var runner = new PlaywrightRunner(_playwright.Page!);
                await runner.ExecuteAsync(actions);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Console.WriteLine($"\n  ❌ Attempt {attempt} failed: {FirstLine(ex.Message)}");

                if (attempt > MaxRetries)
                    Console.WriteLine($"  🚫 Exhausted all {MaxRetries} retry attempt(s)");
            }
        }

        throw lastError!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Self-healing
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<PlaywrightAction>> HealActionsAsync(
        List<PlaywrightAction> failedActions,
        string scenarioText,
        Exception failure)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The Playwright action plan below FAILED.");
        sb.AppendLine("Analyse the error, identify the broken action, and return a fully corrected plan.");
        sb.AppendLine();
        sb.AppendLine($"FAILURE: {FirstLine(failure.Message)}");
        sb.AppendLine();
        sb.AppendLine("ORIGINAL SCENARIO:");
        sb.AppendLine(scenarioText);
        sb.AppendLine();
        sb.AppendLine("FAILED ACTION PLAN (JSON):");
        sb.AppendLine(JsonConvert.SerializeObject(failedActions, Formatting.Indented));
        sb.AppendLine();
        sb.AppendLine("SELF-HEAL GUIDANCE:");
        sb.AppendLine("  strict mode / resolved to N elements → add or correct the 'index' field");
        sb.AppendLine("  element not found / waiting for locator → try different locatorType or locatorName");
        sb.AppendLine("  expected to contain text / not found  → locator or expected text is wrong");
        sb.AppendLine("  checkbox no accessible name           → locatorName must be \"\" with index");
        sb.AppendLine("  timeout                               → insert wait_for_url or assert_visible first");
        sb.AppendLine();
        sb.Append("Return ONLY the corrected JSON action array — same schema, no explanation.");

        Console.WriteLine("  🧠 Asking Claude to self-heal the action plan...");
        var raw    = await _claude.GenerateAsync(SystemPrompts.TestActionGenerator, sb.ToString());
        var healed = ParseActions(raw, "healed");

        Console.WriteLine($"  ✅ Healed plan: {healed.Count} action(s)");
        PrintActionPlan(healed);
        return healed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Allure artefact attachment
    // ─────────────────────────────────────────────────────────────────────────

    private static void AttachGeneratedCode(string featureTitle)
    {
        var root          = FindProjectRoot();
        var pageClassName = DerivePageClassName(featureTitle);
        var stepClassName = pageClassName.Replace("Page", "Steps");

        var pageFile = Path.Combine(root, "Pages",           $"{pageClassName}.generated.cs");
        var stepFile = Path.Combine(root, "StepDefinitions", $"{stepClassName}.generated.cs");

        AllureReporter.AttachGeneratedFile(pageFile, $"Generated: {pageClassName}.cs");
        AllureReporter.AttachGeneratedFile(stepFile, $"Generated: {stepClassName}.cs");
    }

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

    // ─────────────────────────────────────────────────────────────────────────
    // Page reset
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ResetPageAsync()
    {
        try
        {
            await _playwright.BrowserContext!.ClearCookiesAsync();
            await _playwright.Page!.GotoAsync("about:blank");
            Console.WriteLine("  🔁 Page reset (cookies cleared)");
        }
        catch
        {
            try
            {
                _playwright.Page = await _playwright.BrowserContext!.NewPageAsync();
                Console.WriteLine("  🔁 Fresh page created for retry");
            }
            catch { /* non-critical */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<PlaywrightAction> ParseActions(string rawJson, string label)
    {
        var json = rawJson.Trim();
        if (json.StartsWith("```"))
        {
            var lines = json.Split('\n');
            json = string.Join("\n",
                lines.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```")));
        }

        return JsonConvert.DeserializeObject<List<PlaywrightAction>>(json.Trim())
               ?? throw new InvalidOperationException(
                   $"Claude returned invalid JSON ({label}):\n{rawJson}");
    }

    private static string FirstLine(string message) =>
        message.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static void PrintBanner(string title)
    {
        Console.WriteLine($"\n  {"─",60}");
        Console.WriteLine($"  🤖 AI Playwright Framework");
        Console.WriteLine($"  📋 {title}");
        Console.WriteLine($"  {"─",60}\n");
    }

    private static void PrintScenarioBlock(string text)
    {
        Console.WriteLine("  ┌─ Scenario ─────────────────────────────────────────────");
        foreach (var l in text.Split('\n'))
            Console.WriteLine($"  │  {l}");
        Console.WriteLine("  └────────────────────────────────────────────────────────\n");
    }

    private static void PrintActionPlan(List<PlaywrightAction> actions)
    {
        Console.WriteLine("  Action plan:");
        for (var i = 0; i < actions.Count; i++)
        {
            var a      = actions[i];
            var target = a.LocatorType is not null ? $"  [{a.LocatorType}:{a.LocatorName}]" : string.Empty;
            Console.WriteLine($"    {i + 1:D2}. {a.Action,-16}{target}");
        }
        Console.WriteLine();
    }
}