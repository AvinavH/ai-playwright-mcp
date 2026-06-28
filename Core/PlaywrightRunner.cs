using Playwright.AiFramework.Reporting;

namespace Playwright.AiFramework.Core;

/// <summary>
/// Executes AI-generated Playwright actions against a live browser page.
/// Executes AI-generated Playwright actions against a live browser page.
///
/// Locator strategy — smart and adaptive:
///
///   role + name  → tries Exact = true first (best practice)
///                  if 0 elements found and Exact was not explicitly set,
///                  automatically retries with Exact = false (adaptive fallback)
///
///   role + no name → omits the Name filter entirely
///                    (correct for un-named elements like checkboxes without labels)
///
///   text         → Exact = false by default (partial match; avoids strict-mode
///                  violations caused by text appearing in multiple page regions)
///                  Set "exact": true in the action to force an exact match.
///
///   label        → Exact = false by default (labels are unique per form,
///                  partial match is safe and avoids whitespace issues)
///
///   placeholder, testid → no Exact option (Playwright handles these internally)
/// 
/// Allure integration:
///   • Every action is wrapped in AllureReporter.StepAsync — appears as a named
///     sub-step in the Allure report with pass/fail status and timing.
///   • Screenshots are taken after key actions (navigate, assertions) and attached
///     to the step so the report shows the page state at each meaningful point.
///   • Screenshot frequency is controlled by Allure:ScreenshotMode in appsettings.json:
///       KeyActions (default) — navigate + assert_* only
///       All                  — after every action
///       None                 — no inline screenshots (failure screenshot still captured)
/// </summary>
public class PlaywrightRunner
{
    private readonly IPage  _page;
    private readonly string _screenshotMode;

    public PlaywrightRunner(IPage page)
    {
        _page           = page;
        _screenshotMode = AppConfig.Get("Allure:ScreenshotMode") ?? "KeyActions";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task ExecuteAsync(IReadOnlyList<PlaywrightAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var a        = actions[i];
            var stepName = FormatStepName(a, i + 1, actions.Count);

            Console.WriteLine($"  [{i + 1:D2}] {a.Description ?? a.Action.ToUpperInvariant()}");

            // Each Playwright action becomes a named sub-step in Allure.
            // The step shows timing, pass/fail, and any screenshot attachments.
            await AllureReporter.StepAsync(stepName, async () =>
            {
                await RunActionAsync(a);

                if (ShouldCapture(a.Action))
                    await AllureReporter.AttachScreenshotAsync(
                        _page,
                        $"{ActionIcon(a.Action)} {a.Description ?? a.Action}");
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Action dispatcher
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunActionAsync(PlaywrightAction a)
    {
        switch (a.Action.ToLowerInvariant())
        {
            case "navigate":
                await _page.GotoAsync(Require(a.Url, a, "url"));
                return;

            case "assert_url":
                await Assertions.Expect(_page)
                    .ToHaveURLAsync(new Regex(Require(a.Expected, a, "expected")));
                return;

            case "wait_for_url":
                await _page.WaitForURLAsync(Require(a.Url, a, "url"));
                return;
        }

        var loc = await ResolveLocatorAsync(a);

        switch (a.Action.ToLowerInvariant())
        {
            case "fill":
                await loc.FillAsync(Require(a.Value, a, "value"));
                break;

            case "click":
                await loc.ClickAsync();
                break;

            case "check":
                await loc.CheckAsync();
                break;

            case "uncheck":
                await loc.UncheckAsync();
                break;

            case "select":
                await loc.SelectOptionAsync(Require(a.Value, a, "value"));
                break;

            case "assert_visible":
                await Assertions.Expect(loc).ToBeVisibleAsync();
                break;

            case "assert_text":
                if (!string.IsNullOrEmpty(a.Expected))
                    await Assertions.Expect(loc).ToContainTextAsync(a.Expected);
                else
                    await Assertions.Expect(loc).ToBeVisibleAsync();
                break;

            case "assert_checked":
                await Assertions.Expect(loc).ToBeCheckedAsync();
                break;

            default:
                throw new NotSupportedException($"Unknown action: '{a.Action}'");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Adaptive locator resolution
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ILocator> ResolveLocatorAsync(PlaywrightAction a)
    {
        var type        = LocatorType(a);
        var preferExact = type.StartsWith("role:") && !string.IsNullOrEmpty(a.LocatorName);
        var exact       = a.Exact ?? preferExact;

        var locator  = BuildLocator(a, exact);
        var resolved = ApplyNth(locator, a);

        if (exact && a.Exact is null)
        {
            var count = await resolved.CountAsync();
            if (count == 0)
            {
                var fallback         = BuildLocator(a, exact: false);
                var fallbackResolved = ApplyNth(fallback, a);
                var fallbackCount    = await fallbackResolved.CountAsync();

                if (fallbackCount > 0)
                {
                    Console.WriteLine(
                        $"  ℹ️  Exact→0, partial→{fallbackCount} [{a.LocatorType}:{a.LocatorName}]");
                    return fallbackResolved;
                }
            }
        }

        return resolved;
    }

    private ILocator BuildLocator(PlaywrightAction a, bool exact)
    {
        var type = LocatorType(a);
        var name = a.LocatorName;

        if (type.StartsWith("role:"))
        {
            var ariaRole = Enum.Parse<AriaRole>(type[5..], ignoreCase: true);
            var options  = new PageGetByRoleOptions();
            if (!string.IsNullOrEmpty(name))
            {
                options.Name  = name;
                options.Exact = exact;
            }
            return _page.GetByRole(ariaRole, options);
        }

        if (string.IsNullOrEmpty(name))
            throw new ArgumentException(
                $"'locatorName' must not be empty for locatorType '{type}'");

        return type switch
        {
            "text"        => _page.GetByText(name,        new PageGetByTextOptions  { Exact = exact }),
            "label"       => _page.GetByLabel(name,       new PageGetByLabelOptions { Exact = exact }),
            "placeholder" => _page.GetByPlaceholder(name),
            "testid"      => _page.GetByTestId(name),
            _             => throw new NotSupportedException($"Unsupported locatorType: '{type}'")
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private bool ShouldCapture(string action) =>
        _screenshotMode switch
        {
            "All"        => true,
            "KeyActions" => action is "navigate" or "assert_visible" or "assert_text" or "assert_url",
            _            => false   // "None" or unrecognised
        };

    private static string ActionIcon(string action) => action switch
    {
        "navigate"       => "🌐",
        "fill"           => "✏️",
        "click"          => "🖱️",
        "check"          => "☑️",
        "uncheck"        => "🔲",
        "select"         => "📋",
        "assert_visible" => "👁️",
        "assert_text"    => "🔤",
        "assert_checked" => "✅",
        "assert_url"     => "🔗",
        _                => "▶️"
    };

    private static string FormatStepName(PlaywrightAction a, int index, int total)
    {
        var icon   = ActionIcon(a.Action);
        var desc   = a.Description ?? $"{a.Action.ToUpperInvariant()} {a.LocatorName}".Trim();
        return $"{icon} [{index}/{total}] {desc}";
    }

    private static ILocator ApplyNth(ILocator locator, PlaywrightAction a) =>
        a.Index.HasValue ? locator.Nth(a.Index.Value) : locator;

    private static string LocatorType(PlaywrightAction a) =>
        (a.LocatorType
         ?? throw new ArgumentException($"'locatorType' required for '{a.Action}'"))
        .ToLowerInvariant();

    private static string Require(string? value, PlaywrightAction action, string field) =>
        value ?? throw new ArgumentException($"Action '{action.Action}' requires '{field}'.");
}