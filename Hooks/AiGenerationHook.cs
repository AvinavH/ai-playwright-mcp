namespace Playwright.AiFramework.Hooks;

/// <summary>
/// The AI orchestration hook — runs once per @ai_generated scenario in BeforeScenario.
///
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
/// </summary>
[Binding]
public class AiGenerationHook
{
    private readonly PlaywrightContext _playwright;
    private readonly ScenarioContext   _scenario;
    private readonly AnthropicClient   _claude;
    private readonly ActionCache       _cache;
    private readonly FeatureFileReader _featureReader;

    public AiGenerationHook(PlaywrightContext playwright, ScenarioContext scenario)
    {
        _playwright    = playwright;
        _scenario      = scenario;
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
    // Main hook — runs before every @ai_generated scenario
    // ─────────────────────────────────────────────────────────────────────────

    [BeforeScenario("ai_generated", Order = 10)]
    public async Task GenerateAndRunAsync()
    {
        var title = _scenario.ScenarioInfo.Title;

        PrintBanner(title);

        // ── Step 1: Try cache ─────────────────────────────────────────────────
        var actions = await _cache.TryGetAsync(title);

        if (actions is null)
        {
            // ── Step 2: Read scenario from .feature file ──────────────────────
            Console.WriteLine("  📄 Reading scenario from feature file...");
            var scenarioText = await _featureReader.GetScenarioTextAsync(title);

            Console.WriteLine("  🧠 Sending to Claude for Playwright action generation...\n");
            Console.WriteLine("  ┌─ Scenario text sent to Claude ─────────────────────────");
            foreach (var l in scenarioText.Split('\n'))
                Console.WriteLine($"  │  {l}");
            Console.WriteLine("  └────────────────────────────────────────────────────────\n");

            // ── Step 3: Call Claude API ───────────────────────────────────────
            var rawJson = await _claude.GenerateActionsAsync(scenarioText);

            // ── Step 4: Parse response ────────────────────────────────────────
            actions = ParseActions(rawJson, title);
            Console.WriteLine($"  ✅ Claude generated {actions.Count} Playwright actions\n");

            PrintActionPlan(actions);

            // ── Step 5: Persist to disk ───────────────────────────────────────
            await _cache.SaveAsync(title, actions);
        }

        // ── Step 6: Execute via Playwright ────────────────────────────────────
        Console.WriteLine($"\n  ▶  Executing {actions.Count} actions in browser:\n");
        var runner = new PlaywrightRunner(_playwright.Page!);
        await runner.ExecuteAsync(actions);

        _scenario["AiActionsExecuted"] = true;
        Console.WriteLine("\n  🎉 All actions completed\n");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<PlaywrightAction> ParseActions(string rawJson, string scenarioTitle)
    {
        // Strip accidental markdown code fences (```json ... ```)
        var json = rawJson.Trim();
        if (json.StartsWith("```"))
        {
            var lines = json.Split('\n');
            json = string.Join("\n",
                lines.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```")));
        }

        return JsonConvert.DeserializeObject<List<PlaywrightAction>>(json.Trim())
               ?? throw new InvalidOperationException(
                   $"Claude returned invalid JSON for scenario '{scenarioTitle}':\n{rawJson}");
    }

    private static void PrintBanner(string title)
    {
        const int width = 60;
        Console.WriteLine($"\n  {'─',width}");
        Console.WriteLine($"  🤖 AI Playwright Framework");
        Console.WriteLine($"  📋 {title}");
        Console.WriteLine($"  {'─',width}\n");
    }

    private static void PrintActionPlan(List<PlaywrightAction> actions)
    {
        Console.WriteLine("  Generated action plan:");
        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            var locator = a.LocatorType is not null
                ? $"  [{a.LocatorType}:{a.LocatorName}]"
                : string.Empty;
            Console.WriteLine($"    {i + 1:D2}. {a.Action,-16}{locator}");
        }
        Console.WriteLine();
    }
}
