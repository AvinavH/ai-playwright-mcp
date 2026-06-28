using Allure.Net.Commons;
using Playwright.AiFramework.Reporting;

namespace Playwright.AiFramework.Hooks;

[Binding]
public static class AllureGlobalSetup
{
    // allureConfig.json redirects test-result JSON files to {projectRoot}/allure-results
    // via "directory": "../../../allure-results" (3 levels up from bin/Debug/net8.0/).
    // AllureResultsDir must point to the SAME place so environment.properties lands
    // alongside the test-result files and allure serve sees everything in one folder.
    private static readonly string AllureResultsDir =
        Path.Combine(FindProjectRoot(), "allure-results");

    [BeforeTestRun]
    public static void ConfigureAllure()
    {
        Console.WriteLine($"\n[Allure] Results directory → {AllureResultsDir}");

        AllureReporter.WriteEnvironmentProperties(AllureResultsDir,
            new Dictionary<string, string>
            {
                ["Browser"]         = "Chromium",
                ["Headless"]        = AppConfig.Get("Browser:Headless")      ?? "false",
                ["SlowMo_ms"]       = AppConfig.Get("Browser:SlowMo")        ?? "50",
                ["Base_URL"]        = AppConfig.Get("Browser:BaseUrl")        ?? "https://the-internet.herokuapp.com",
                ["Claude_Model"]    = "claude-sonnet-4-6",
                ["Screenshot_Mode"] = AppConfig.Get("Allure:ScreenshotMode") ?? "KeyActions",
                ["Framework"]       = "AI Playwright C# + Reqnroll 3"
            });

        AllureReporter.CopyCategories(
            Path.Combine(FindProjectRoot(), "allure-categories.json"),
            AllureResultsDir);
    }

    [AfterTestRun]
    public static void PrintHint()
    {
        Console.WriteLine("\n  ───────────────────────────────────────────────────────");
        Console.WriteLine("  📊 Allure report ready.");
        Console.WriteLine($"     Results  → {AllureResultsDir}");
        Console.WriteLine("     View     → allure serve allure-results");
        Console.WriteLine("     Generate → allure generate allure-results -o allure-report --clean");
        Console.WriteLine("  ───────────────────────────────────────────────────────\n");
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}