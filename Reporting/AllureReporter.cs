using Allure.Net.Commons;

namespace Playwright.AiFramework.Reporting;
/// <summary>
/// Central facade for all Allure reporting operations in the framework.
///
/// Responsibilities:
///   • Wrap async Playwright actions as named Allure steps with pass/fail status
///   • Attach AI-generated action plans (JSON) to the test case
///   • Attach screenshots at key points (navigate, assertions, failures)
///   • Attach generated .cs artefacts (Page Objects, Step Definitions)
///   • Write environment.properties for the Allure environment panel
/// </summary>
public static class AllureReporter
{
    // ── Step wrapping ─────────────────────────────────────────────────────────
    /// <summary>
    /// Executes an async action as a named Allure step.
    /// Status is set to passed on success, failed on exception (which is re-thrown).
    /// Uses AllureLifecycle directly for full async compatibility.
    /// </summary>
    public static async Task StepAsync(string name, Func<Task> asyncAction)
    {
        AllureLifecycle.Instance.StartStep(new StepResult
        {
            name  = name,
            stage = Stage.running
        });
        try
        {
            await asyncAction();
            AllureLifecycle.Instance.StopStep(s =>
            {
                s.status = Status.passed;
                s.stage  = Stage.finished;
            });
        }
        catch (Exception ex)
        {
            AllureLifecycle.Instance.StopStep(s =>
            {
                s.status        = Status.failed;
                s.stage         = Stage.finished;
                s.statusDetails = new StatusDetails
                {
                    message = ex.Message.Split('\n')[0],
                    trace   = ex.StackTrace
                };
            });
            throw;
        }
    }

    // ── Attachments ───────────────────────────────────────────────────────────

    public static void AttachActionPlan(IReadOnlyList<PlaywrightAction> actions, string label)
    {
        try
        {
            var json  = JsonConvert.SerializeObject(actions, Formatting.Indented);
            var bytes = Encoding.UTF8.GetBytes(json);
            AllureApi.AddAttachment(label, "application/json", bytes, ".json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not attach action plan — {ex.Message}");
        }
    }

    public static async Task AttachScreenshotAsync(IPage page, string name)
    {
        try
        {
            var bytes = await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = true });
            AllureApi.AddAttachment(name, "image/png", bytes, ".png");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: screenshot skipped — {ex.Message}");
        }
    }

    /// <summary>
    /// Attaches a generated .cs file to the Allure report.
    ///
    /// IMPORTANT: the extension is intentionally .txt — NOT .cs.
    /// Allure saves attachments as {uuid}-attachment.{ext} inside allure-results/.
    /// If .cs is used, the .NET SDK glob **/*.cs compiles those copies alongside
    /// the real source files and produces CS0101 duplicate-class errors.
    /// .txt produces identical display in the Allure UI (content is shown as
    /// plain text regardless of extension) while keeping the compiler happy.
    /// </summary>
    public static void AttachGeneratedFile(string filePath, string label)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            AllureApi.AddAttachment(label, "text/plain", File.ReadAllBytes(filePath), ".txt");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not attach {label} — {ex.Message}");
        }
    }

    public static void AttachText(string content, string label)
    {
        try
        {
            AllureApi.AddAttachment(label, "text/plain", Encoding.UTF8.GetBytes(content), ".txt");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not attach text — {ex.Message}");
        }
    }

    // ── Environment ───────────────────────────────────────────────────────────

    public static void WriteEnvironmentProperties(
        string allureResultsDir, Dictionary<string, string> properties)
    {
        try
        {
            Directory.CreateDirectory(allureResultsDir);
            File.WriteAllLines(
                Path.Combine(allureResultsDir, "environment.properties"),
                properties.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not write environment properties — {ex.Message}");
        }
    }

    public static void CopyCategories(string sourceFile, string allureResultsDir)
    {
        try
        {
            if (!File.Exists(sourceFile)) return;
            Directory.CreateDirectory(allureResultsDir);
            File.Copy(sourceFile,
                Path.Combine(allureResultsDir, "categories.json"), overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not copy categories — {ex.Message}");
        }
    }
}