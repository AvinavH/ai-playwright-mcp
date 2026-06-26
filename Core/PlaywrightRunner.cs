namespace Playwright.AiFramework.Core;

/// <summary>
/// Executes a list of <see cref="PlaywrightAction"/> objects against a live Playwright IPage.
///
/// Locator resolution follows Playwright best-practice priority:
///   GetByRole  →  GetByLabel  →  GetByText  →  GetByPlaceholder  →  GetByTestId
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
        switch (a.Action.ToLowerInvariant())
        {
            case "navigate":
                await _page.GotoAsync(Require(a.Url, a, "url"));
                break;

            case "fill":
                await Locator(a).FillAsync(Require(a.Value, a, "value"));
                break;

            case "click":
                await Locator(a).ClickAsync();
                break;

            case "check":
                await Locator(a).CheckAsync();
                break;

            case "uncheck":
                await Locator(a).UncheckAsync();
                break;

            case "select":
                await Locator(a).SelectOptionAsync(Require(a.Value, a, "value"));
                break;

            case "assert_visible":
                await Assertions.Expect(Locator(a)).ToBeVisibleAsync();
                break;

            case "assert_text":
                await Assertions.Expect(Locator(a))
                    .ToContainTextAsync(Require(a.Expected, a, "expected"));
                break;

            case "assert_checked":
                await Assertions.Expect(Locator(a)).ToBeCheckedAsync();
                break;

            case "assert_url":
                await Assertions.Expect(_page)
                    .ToHaveURLAsync(new Regex(Require(a.Expected, a, "expected")));
                break;

            case "wait_for_url":
                await _page.WaitForURLAsync(Require(a.Url, a, "url"));
                break;

            default:
                throw new NotSupportedException(
                    $"Unknown action type: '{a.Action}'. " +
                    $"Supported: navigate, fill, click, check, uncheck, select, " +
                    $"assert_visible, assert_text, assert_checked, assert_url, wait_for_url");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Locator resolution
    // ─────────────────────────────────────────────────────────────────────────

    private ILocator Locator(PlaywrightAction a)
    {
        var type = (a.LocatorType
                    ?? throw new ArgumentException(
                        $"'locatorType' is required for action '{a.Action}'"))
            .ToLowerInvariant();

        var name = a.LocatorName; // May be null/empty for un-named elements (e.g. checkboxes)

        ILocator locator;

        // ── GetByRole  ────────────────────────────────────────────────────────
        if (type.StartsWith("role:"))
        {
            var roleStr  = type[5..]; // e.g. "button", "checkbox", "heading"
            var ariaRole = Enum.Parse<AriaRole>(roleStr, ignoreCase: true);

            var options = new PageGetByRoleOptions();
            if (!string.IsNullOrEmpty(name))
                options.Name = name;

            locator = _page.GetByRole(ariaRole, options);
        }
        else
        {
            // All other strategies require a non-empty name
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(
                    $"'locatorName' must not be empty for locatorType '{type}'");

            locator = type switch
            {
                "label"       => _page.GetByLabel(name),
                "text"        => _page.GetByText(name),
                "placeholder" => _page.GetByPlaceholder(name),
                "testid"      => _page.GetByTestId(name),
                _             => throw new NotSupportedException(
                                     $"Unsupported locatorType: '{type}'. " +
                                     $"Use: role:<ariarole>, label, text, placeholder, testid")
            };
        }

        // Apply .Nth(n) if an index was provided (handles duplicate elements)
        return a.Index.HasValue ? locator.Nth(a.Index.Value) : locator;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string Require(string? value, PlaywrightAction action, string fieldName)
        => value ?? throw new ArgumentException(
               $"Action '{action.Action}' requires the '{fieldName}' field.");
}
