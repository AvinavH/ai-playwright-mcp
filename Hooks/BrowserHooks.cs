using Playwright.AiFramework.Reporting;
using Reqnroll.BoDi;

namespace Playwright.AiFramework.Hooks;

/// <summary>
/// Manages the Playwright browser lifecycle for every scenario.
/// Captures a full-page screenshot on test failure.
///
/// Allure integration:
///   • Failure screenshots are attached directly to the Allure test case
///     in addition to being saved to disk, so the report is self-contained.
///
/// IObjectContainer registration:
///   After creating the IPage, we register it with Reqnroll's BoDi container.
///   This allows Page Object constructors (e.g. LoginPage(IPage page)) to be
///   resolved by Reqnroll's DI when scenarios run without the @ai_generated tag
///   (i.e. after the dev has graduated the feature to concrete step definitions).
///
///   Without this registration, BoDi throws:
///     "Interface cannot be resolved: Microsoft.Playwright.IPage
///      (resolution path: LoginSteps → LoginPage)"
/// </summary>
[Binding]
public class BrowserHooks
{
    private readonly PlaywrightContext _context;
    private readonly ScenarioContext   _scenario;
    private readonly IObjectContainer  _container;   // ← Reqnroll's DI container

    public BrowserHooks(
        PlaywrightContext context,
        ScenarioContext   scenario,
        IObjectContainer  container)                 // ← injected automatically by Reqnroll
    {
        _context   = context;
        _scenario  = scenario;
        _container = container;
    }

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

        // Register IPage with Reqnroll's DI container so that Page Object classes
        // (e.g. LoginPage, CheckboxesPage) can receive it via constructor injection
        // when Reqnroll resolves step definition bindings for graduated scenarios.
        _container.RegisterInstanceAs<IPage>(_context.Page);

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

    private async Task CaptureFailureScreenshotAsync()
    {
        // Attach to Allure report (embedded in the HTML report)
        await AllureReporter.AttachScreenshotAsync(_context.Page!, "❌ Failure Screenshot");

        // Also save to disk for archiving / CI artefact upload
        try
        {
            var dir      = Path.Combine(AppContext.BaseDirectory, "Screenshots");
            Directory.CreateDirectory(dir);
            var fileName = $"{Sanitise(_scenario.ScenarioInfo.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await _context.Page!.ScreenshotAsync(
                new PageScreenshotOptions { Path = Path.Combine(dir, fileName), FullPage = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Screenshot save failed: {ex.Message}");
        }
    }

    private static string Sanitise(string name) =>
        new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
}