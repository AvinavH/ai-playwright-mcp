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
    // ─────────────────────────────────────────────────────────────────────────
    // Step wrapping
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes an async action as a named Allure step.
    /// Status is set to passed on success, failed on exception (which is re-thrown).
    /// Uses AllureLifecycle directly for full async compatibility.
    /// </summary>
    public static async Task StepAsync(string name, Func<Task> asyncAction)
{
    // No UUID — AllureLifecycle 2.x manages step identity internally
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

    // ─────────────────────────────────────────────────────────────────────────
    // Attachments
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches the AI-generated action plan as a JSON attachment.
    /// Visible in the Allure report under "Attachments" for the test case.
    /// </summary>
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

    /// <summary>
    /// Captures a full-page screenshot and attaches it to the current test/step.
    /// Silent on failure — a missing screenshot is not worth failing the report.
    /// </summary>
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
    /// Attaches a generated .cs file so reviewers can inspect code artefacts
    /// directly in the Allure report without opening the project.
    /// </summary>
    public static void AttachGeneratedFile(string filePath, string label)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            var bytes = File.ReadAllBytes(filePath);
            AllureApi.AddAttachment(label, "text/plain", bytes, ".cs");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not attach {label} — {ex.Message}");
        }
    }

    /// <summary>
    /// Attaches a plain-text error description as a step-level note.
    /// Useful for surfacing retry failure reasons clearly in the report.
    /// </summary>
    public static void AttachText(string content, string label)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            AllureApi.AddAttachment(label, "text/plain", bytes, ".txt");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not attach text — {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Environment panel
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes environment.properties to the allure-results directory.
    /// This populates the "Environment" panel visible on the Allure overview page.
    /// Must be called once before tests run (use a [BeforeTestRun] hook).
    /// </summary>
    public static void WriteEnvironmentProperties(string allureResultsDir,
        Dictionary<string, string> properties)
    {
        try
        {
            Directory.CreateDirectory(allureResultsDir);
            var lines = properties.Select(kv => $"{kv.Key}={kv.Value}");
            File.WriteAllLines(
                Path.Combine(allureResultsDir, "environment.properties"), lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not write environment properties — {ex.Message}");
        }
    }

    /// <summary>
    /// Copies a categories.json file into allure-results so Allure's failure
    /// triage view can group errors by pattern on report generation.
    /// </summary>
    public static void CopyCategories(string sourceFile, string allureResultsDir)
    {
        try
        {
            if (!File.Exists(sourceFile)) return;
            Directory.CreateDirectory(allureResultsDir);
            File.Copy(sourceFile,
                Path.Combine(allureResultsDir, "categories.json"),
                overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️  Allure: could not copy categories — {ex.Message}");
        }
    }
}