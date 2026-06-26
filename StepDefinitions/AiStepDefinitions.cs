namespace Playwright.AiFramework.StepDefinitions;

/// <summary>
/// Wildcard step definitions — exclusively bound to @ai_generated scenarios via [Scope].
///
/// Why these exist:
///   Reqnroll requires every step in a scenario to have a matching binding before it
///   will run ANY hooks. Without these bindings, Reqnroll would report
///   "No matching step definition" and skip the scenario entirely.
///
/// Why they are no-ops:
///   AiGenerationHook.BeforeScenario (Order=10) already ran the entire scenario
///   through Playwright BEFORE any of these step methods are called.
///   These methods simply log the step name so Reqnroll reports each step as passed.
///
/// Interview talking point:
///   "The framework separates concerns: the AI + Playwright execution happens in
///    the BeforeScenario hook; the step bindings exist purely to satisfy Reqnroll's
///    binding contract and to surface each step as a distinct pass in the report."
/// </summary>
[Binding]
[Scope(Tag = "ai_generated")]   // Only applies to scenarios tagged @ai_generated
public class AiStepDefinitions
{
    private readonly ScenarioContext _scenario;

    public AiStepDefinitions(ScenarioContext scenario) => _scenario = scenario;

    [Given(@"(.*)")]
    public void AiGiven(string step) => Report("Given", step);

    [When(@"(.*)")]
    public void AiWhen(string step) => Report("When", step);

    [Then(@"(.*)")]
    public void AiThen(string step) => Report("Then", step);

    // ─────────────────────────────────────────────────────────────────────────

    private void Report(string keyword, string step)
    {
        if (!_scenario.ContainsKey("AiActionsExecuted"))
            throw new InvalidOperationException(
                "AiGenerationHook did not complete before steps ran. " +
                "Ensure the @ai_generated tag is present and the API key is configured.");

        Console.WriteLine($"  ✓ {keyword} {step}");
    }
}
