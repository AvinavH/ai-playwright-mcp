namespace Playwright.AiFramework.Core;

/// <summary>
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
/// </summary>
public class PlaywrightRunner
{
    private readonly IPage _page;

    public PlaywrightRunner(IPage page) => _page = page;

    // ─────────────────────────────────────────────────────────────────────────
    // Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task ExecuteAsync(IReadOnlyList<PlaywrightAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            Console.WriteLine($"  [{i + 1:D2}] {a.Description ?? a.Action.ToUpperInvariant()}");
            await RunActionAsync(a);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Action dispatcher
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunActionAsync(PlaywrightAction a)
    {
        // ── Page-level actions (no locator needed) ────────────────────────────
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

        // ── Locator-based actions ─────────────────────────────────────────────
        // Resolve once with adaptive retry, then dispatch on action type.
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
                // Fix: if Claude omits 'expected', degrade gracefully to a visibility check
                // rather than passing empty string to ToContainTextAsync (which finds nothing)
                if (!string.IsNullOrEmpty(a.Expected))
                    await Assertions.Expect(loc).ToContainTextAsync(a.Expected);
                else
                    await Assertions.Expect(loc).ToBeVisibleAsync();
                break;

            case "assert_checked":
                await Assertions.Expect(loc).ToBeCheckedAsync();
                break;

            default:
                throw new NotSupportedException(
                    $"Unknown action: '{a.Action}'. Supported: navigate, fill, click, " +
                    "check, uncheck, select, assert_visible, assert_text, " +
                    "assert_checked, assert_url, wait_for_url");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Adaptive locator resolution
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two-phase resolution:
    ///   Phase 1 — build with preferred settings (Exact = true for role+name)
    ///   Phase 2 — if 0 elements found AND Exact was auto-selected (not explicitly
    ///             set in the action), rebuild with Exact = false and log the fallback.
    ///
    /// CountAsync() is used rather than try/catch because it is non-throwing
    /// and does not trigger Playwright's built-in timeout/retry mechanism.
    /// </summary>
    private async Task<ILocator> ResolveLocatorAsync(PlaywrightAction a)
    {
        var type = LocatorType(a);

        // Exact = true is preferred only for role locators that have an explicit name.
        // text / label / everything else defaults to partial match.
        var preferExact = type.StartsWith("role:") && !string.IsNullOrEmpty(a.LocatorName);
        var exact       = a.Exact ?? preferExact;

        var locator  = BuildLocator(a, exact);
        var resolved = ApplyNth(locator, a);

        // Adaptive retry — only when Exact was auto-chosen, not when the action
        // explicitly set "exact": true (meaning the caller knows what they want).
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
                        $"  ℹ️  Exact match: 0 results — partial match: {fallbackCount} result(s) " +
                        $"[{a.LocatorType}:{a.LocatorName}]");
                    return fallbackResolved;
                }
            }
        }

        return resolved;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pure locator construction (no async, no side-effects)
    // ─────────────────────────────────────────────────────────────────────────

    private ILocator BuildLocator(PlaywrightAction a, bool exact)
    {
        var type = LocatorType(a);
        var name = a.LocatorName;

        // ── GetByRole ─────────────────────────────────────────────────────────
        if (type.StartsWith("role:"))
        {
            var ariaRole = Enum.Parse<AriaRole>(type[5..], ignoreCase: true);
            var options  = new PageGetByRoleOptions();

            // Only apply the Name filter when the action provides a non-empty locatorName.
            // Omitting Name is correct for elements with no accessible name (e.g. checkboxes
            // without an associated <label>) — use the 'index' field to pick the right one.
            if (!string.IsNullOrEmpty(name))
            {
                options.Name  = name;
                options.Exact = exact;
            }

            return _page.GetByRole(ariaRole, options);
        }

        // All non-role strategies require a non-empty name
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException(
                $"'locatorName' must not be empty for locatorType '{type}'");

        return type switch
        {
            // Partial match by default — text fragments often appear inside longer strings
            "text"        => _page.GetByText(name, new PageGetByTextOptions { Exact = exact }),

            // Partial match by default — form labels are unique per page section
            "label"       => _page.GetByLabel(name, new PageGetByLabelOptions { Exact = exact }),

            // No Exact option in Playwright API for these two
            "placeholder" => _page.GetByPlaceholder(name),
            "testid"      => _page.GetByTestId(name),

            _ => throw new NotSupportedException(
                     $"Unsupported locatorType: '{type}'. " +
                     "Valid values: role:<ariarole>, label, text, placeholder, testid")
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static ILocator ApplyNth(ILocator locator, PlaywrightAction a) =>
        a.Index.HasValue ? locator.Nth(a.Index.Value) : locator;

    private static string LocatorType(PlaywrightAction a) =>
        (a.LocatorType
         ?? throw new ArgumentException($"'locatorType' is required for action '{a.Action}'"))
        .ToLowerInvariant();

    private static string Require(string? value, PlaywrightAction action, string fieldName) =>
        value ?? throw new ArgumentException(
            $"Action '{action.Action}' requires the '{fieldName}' field.");
}