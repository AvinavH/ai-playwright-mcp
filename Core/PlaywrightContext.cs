namespace Playwright.AiFramework.Core;

/// <summary>
/// Holds all Playwright objects for the lifetime of a single scenario.
/// Reqnroll creates one instance per scenario and injects it wherever needed
/// (BrowserHooks, AiGenerationHook) via constructor injection.
/// </summary>
public class PlaywrightContext
{
    public IPlaywright?    Playwright    { get; set; }
    public IBrowser?       Browser       { get; set; }
    public IBrowserContext BrowserContext { get; set; } = null!;
    public IPage?          Page          { get; set; }
}
