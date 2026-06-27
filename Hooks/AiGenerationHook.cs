namespace Playwright.AiFramework.Hooks;

/// <summary>
/// Orchestrates AI test generation for every @ai_generated scenario.
/// Flow:
///   1. Read the Gherkin scenario text from the .feature file on disk
///   2. Check the disk cache (GeneratedActions/) — skip API call if already generated
///   3. If no cache → call Claude API with the scenario + system prompt
///   4. Parse the returned JSON into List&lt;PlaywrightAction&gt;
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
    // Main hook
    // ─────────────────────────────────────────────────────────────────────────

    [BeforeScenario("ai_generated", Order = 10)]
    public async Task GenerateAndRunAsync()
    {
        var title        = _scenario.ScenarioInfo.Title;
        var featureTitle = _feature.FeatureInfo.Title;

        PrintBanner(title);

        var scenarioText = await _featureReader.GetScenarioTextAsync(title);

        // ── 1. Get initial action plan (cache or Claude) ──────────────────────
        var actions = await _cache.TryGetAsync(title)
                      ?? await GenerateInitialActionsAsync(scenarioText, title);

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
        }

        // ── 3. Always save code artifacts ─────────────────────────────────────
        // Generated regardless of test outcome so the developer can review/fix.
        Console.WriteLine("\n  📦 Saving code artifacts...");
        try
        {
            var codeGen = new CodeGenerator(_claude);
            await codeGen.GenerateArtifactsAsync(scenarioText, featureTitle);
        }
        catch (Exception codeEx)
        {
            Console.WriteLine($"  ⚠️  Code generation error (non-fatal): {codeEx.Message}");
        }

        // ── 4. Re-throw test failure AFTER artifacts are written ──────────────
        if (testFailure is not null)
        {
            Console.WriteLine("  💾 Page Object and Step Definitions saved for manual review\n");
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

    /// <summary>
    /// Attempts to execute the action plan up to (MaxRetries + 1) times.
    /// After each failure, the failing plan and the Playwright error are sent to
    /// Claude, which returns a corrected plan. The healed plan overwrites the cache
    /// so future cold starts use the best known working version.
    /// </summary>
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
                Console.WriteLine($"\n  🔄 Retry {attempt - 1}/{MaxRetries} — self-healing...");
                await ResetPageAsync();
                actions = await HealActionsAsync(actions, scenarioText, lastError!);
                // Overwrite cache so next cold start uses the healed plan
                await _cache.SaveAsync(cacheKey, actions);
            }

            Console.WriteLine(
                $"\n  ▶  Executing {actions.Count} action(s) " +
                $"(attempt {attempt} of {MaxRetries + 1}):\n");

            try
            {
                var runner = new PlaywrightRunner(_playwright.Page!);
                await runner.ExecuteAsync(actions);
                return; // ← success, exit loop
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

    /// <summary>
    /// Sends the failed action plan + Playwright error to Claude and asks for a
    /// corrected plan. The response is parsed exactly like the initial generation.
    /// </summary>
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
        sb.AppendLine("SELF-HEAL GUIDANCE (match the fix to the error message):");
        sb.AppendLine("  strict mode / resolved to N elements → add or correct the 'index' field");
        sb.AppendLine("  element not found / waiting for locator → try a different locatorType or locatorName");
        sb.AppendLine("  expected to contain text / element not found → the locator or expected text is wrong");
        sb.AppendLine("  checkbox no accessible name → locatorName must be \"\" with index:0 or index:1");
        sb.AppendLine("  timeout → the page may not have loaded; insert a wait_for_url or assert_visible first");
        sb.AppendLine();
        sb.Append("Return ONLY the corrected JSON action array — same schema, no explanation.");

        Console.WriteLine("  🧠 Asking Claude to self-heal the action plan...");
        var raw = await _claude.GenerateAsync(SystemPrompts.TestActionGenerator, sb.ToString());

        var healed = ParseActions(raw, "healed");
        Console.WriteLine($"  ✅ Healed plan: {healed.Count} action(s)");
        PrintActionPlan(healed);
        return healed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Page reset between retries
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ResetPageAsync()
    {
        try
        {
            // Clear session state so the healed plan starts from a known clean position
            await _playwright.BrowserContext!.ClearCookiesAsync();
            await _playwright.Page!.GotoAsync("about:blank");
            Console.WriteLine("  🔁 Page reset (cookies cleared, navigated to blank)");
        }
        catch
        {
            // Page may be in a crashed state — allocate a fresh one
            try
            {
                _playwright.Page = await _playwright.BrowserContext!.NewPageAsync();
                Console.WriteLine("  🔁 Fresh page created for retry");
            }
            catch
            {
                // Non-critical — the retry attempt will surface its own error
            }
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
            var target = a.LocatorType is not null
                ? $"  [{a.LocatorType}:{a.LocatorName}]"
                : string.Empty;
            Console.WriteLine($"    {i + 1:D2}. {a.Action,-16}{target}");
        }
        Console.WriteLine();
    }
}