namespace Playwright.AiFramework.AI;

/// <summary>
/// Represents a single Playwright action returned by Claude.
/// Serialised/deserialised as JSON in the action cache.
/// </summary>
public class PlaywrightAction
{
    /// <summary>
    /// Action type: navigate | fill | click | check | uncheck | select |
    ///              assert_visible | assert_text | assert_checked | assert_url | wait_for_url
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Locator strategy: "role:button" | "role:heading" | "role:checkbox" |
    ///                   "label" | "text" | "placeholder" | "testid"
    /// </summary>
    public string? LocatorType { get; set; }

    /// <summary>Accessible name / visible text for the locator.</summary>
    public string? LocatorName { get; set; }

    /// <summary>
    /// 0-based index for .Nth(n).
    /// REQUIRED for any element that can appear multiple times (Delete buttons, etc.).
    /// </summary>
    public int? Index { get; set; }

    /// <summary>
    /// Fix 4 — Strict mode: whether to use exact matching for role name or text content.
    /// Null → PlaywrightRunner defaults to true (safe default).
    /// Explicitly set false only when a partial/substring match is intentional.
    /// </summary>
    public bool? Exact { get; set; }

    /// <summary>Value to type (fill) or option to select (select).</summary>
    public string? Value { get; set; }

    /// <summary>Expected value used in assertion actions.</summary>
    public string? Expected { get; set; }

    /// <summary>Full URL for navigate / wait_for_url / assert_url.</summary>
    public string? Url { get; set; }

    /// <summary>Human-readable description shown in console output during test run.</summary>
    public string? Description { get; set; }
}