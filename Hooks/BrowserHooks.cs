using Playwright.AiFramework.Reporting;

namespace Playwright.AiFramework.Hooks;

/// <summary>
/// Manages the Playwright browser lifecycle for every scenario.
/// Captures a full-page screenshot on test failure.
///
/// Allure integration:
///   • Failure screenshots are attached directly to the Allure test case
///     in addition to being saved to disk, so the report is self-contained.
/// </summary>
[Binding]
public class BrowserHooks
{
    private readonly PlaywrightContext _context;
    private readonly ScenarioContext   _scenario;

    public BrowserHooks(PlaywrightContext context, ScenarioContext scenario)
    {
        _context  = context;
        _scenario = scenario;
    }

    // ─────────────────────────────────────────────────────────────────────────

    [BeforeScenario(Order = 1)]
    public async Task LaunchBrowserAsync()
    {
        var headless = bool.Parse(AppConfig.Get("Browser:Headless") ?? "false");
        var slowMo   = int.Parse(AppConfig.Get("Browser:SlowMo")   ?? "50");
        var baseUrl  = AppConfig.Get("Browser:BaseUrl")             ?? "https://the-internet.herokuapp.com";

        _context.Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        _context.Browser = await _context.Playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = headless, SlowMo = slowMo });

        _context.BrowserContext = await _context.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL      = baseUrl,
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });

        _context.Page = await _context.BrowserContext.NewPageAsync();

        Console.WriteLine($"  🌐 Browser ready  headless={headless}  slowMo={slowMo}ms  base={baseUrl}");
    }

    [AfterScenario(Order = 99)]
    public async Task CloseBrowserAsync()
    {
        if (_scenario.TestError is not null && _context.Page is not null)
            await CaptureFailureScreenshotAsync();

        if (_context.Page           is not null) await _context.Page.CloseAsync();
        if (_context.BrowserContext is not null) await _context.BrowserContext.CloseAsync();
        if (_context.Browser        is not null) await _context.Browser.CloseAsync();
        _context.Playwright?.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task CaptureFailureScreenshotAsync()
    {
        // 1. Attach to Allure report (embedded in the HTML report)
        await AllureReporter.AttachScreenshotAsync(_context.Page!, "❌ Failure Screenshot");

        // 2. Also save to disk for archiving / CI artefact upload
        try
        {
            var dir      = Path.Combine(AppContext.BaseDirectory, "Screenshots");
            Directory.CreateDirectory(dir);

            var fileName = $"{Sanitise(_scenario.ScenarioInfo.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var path     = Path.Combine(dir, fileName);

            await _context.Page!.ScreenshotAsync(
                new PageScreenshotOptions { Path = path, FullPage = true });

            Console.WriteLine($"  📸 Failure screenshot: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Screenshot save failed: {ex.Message}");
        }
    }

    private static string Sanitise(string name) =>
        new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
}