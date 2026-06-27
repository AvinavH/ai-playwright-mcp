namespace Playwright.AiFramework.AI;

public static class SystemPrompts
{
    public const string TestActionGenerator = """
        You are an expert Playwright C# test automation engineer.
        Convert the Gherkin scenario into a JSON array of Playwright actions
        for https://the-internet.herokuapp.com.

        OUTPUT RULES
        • Return ONLY a valid JSON array — no preamble, no fences, no comments.
        • Every object must have an "action" field and a "description" field.

        ACTION SCHEMAS
        navigate      → { "action":"navigate",       "url":"...",                                              "description":"..." }
        fill          → { "action":"fill",           "locatorType":"...", "locatorName":"...", "value":"...",  "description":"..." }
        click         → { "action":"click",          "locatorType":"...", "locatorName":"...",                 "description":"..." }
        check         → { "action":"check",          "locatorType":"...", "locatorName":"...",                 "description":"..." }
        uncheck       → { "action":"uncheck",        "locatorType":"...", "locatorName":"...",                 "description":"..." }
        select        → { "action":"select",         "locatorType":"...", "locatorName":"...", "value":"...",  "description":"..." }
        assert_visible→ { "action":"assert_visible", "locatorType":"...", "locatorName":"...",                 "description":"..." }
        assert_text   → { "action":"assert_text",    "locatorType":"...", "locatorName":"...", "expected":"...","description":"..." }
        assert_checked→ { "action":"assert_checked", "locatorType":"...", "locatorName":"...",                 "description":"..." }
        assert_url    → { "action":"assert_url",     "expected":"<regex>",                                     "description":"..." }
        wait_for_url  → { "action":"wait_for_url",   "url":"<partial>",                                        "description":"..." }

        OPTIONAL FIELDS
        "index": <int>  — 0-based .Nth(n). Use when multiple elements share the same role+name
                          (e.g. two Delete buttons → index:0 for the first, index:1 for the second).
        "exact": true   — force exact name/text matching. Omit this field in most cases;
                          the framework chooses the right default automatically.
                          Only add it when you need to distinguish between two elements
                          where one name is a substring of the other.

        LOCATOR TYPES (priority order — use the first that applies)
        "role:<ariarole>"  →  GetByRole   e.g. "role:button", "role:heading", "role:checkbox", "role:link"
        "label"            →  GetByLabel  ← PREFERRED for all form inputs that have a visible label
        "text"             →  GetByText   ← for flash messages and body content
        "placeholder"      →  GetByPlaceholder
        "testid"           →  GetByTestId

        CHECKBOX RULE — CRITICAL
        Checkboxes on this site have NO accessible name (no <label> element).
        • Set locatorName to "" (empty string) — do NOT put "checkbox" or any word in locatorName
        • Always use index to target the correct one
        • Example: { "action":"check", "locatorType":"role:checkbox", "locatorName":"", "index":0 }

        REPEATED ELEMENTS RULE
        When a button or element can appear more than once (e.g. Delete buttons added dynamically),
        always include "index": <n> to target the correct occurrence.

        SITE REFERENCE — https://the-internet.herokuapp.com
        /login
          Username input        → locatorType:"label",       locatorName:"Username"
          Password input        → locatorType:"label",       locatorName:"Password"
          Login button          → locatorType:"role:button", locatorName:"Login"
          Success flash message → locatorType:"text",        locatorName:"You logged into a secure area!"
          Failure flash message → locatorType:"text",        locatorName:"Your username is invalid!"

        /checkboxes
          First checkbox  → locatorType:"role:checkbox", locatorName:"", index:0
          Second checkbox → locatorType:"role:checkbox", locatorName:"", index:1

        /dropdown
          Dropdown → locatorType:"role:combobox", locatorName:""

        /add_remove_elements/
          Add button    → locatorType:"role:button", locatorName:"Add Element"
          Delete button → locatorType:"role:button", locatorName:"Delete", index:0
        """;

    // ── Page Object Generator ─────────────────────────────────────────────────
    public static string PageObjectGenerator(string registryContext = "")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a C# Playwright expert generating or updating a Page Object Model class.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(registryContext))
        {
            sb.AppendLine(registryContext);
            sb.AppendLine();
        }

        sb.AppendLine("RULES");
        sb.AppendLine("1.  Namespace:  Playwright.AiFramework.Pages");
        sb.AppendLine("2.  Class name ends in \"Page\"  (e.g. LoginPage, CheckboxesPage)");
        sb.AppendLine("3.  Constructor: public ClassName(IPage page) { _page = page; }");
        sb.AppendLine("4.  One public async Task method per Given/When/Then step");
        sb.AppendLine("5.  Use ONLY: GetByRole, GetByLabel, GetByText, GetByPlaceholder, GetByTestId");
        sb.AppendLine("6.  For checkboxes without labels: GetByRole(AriaRole.Checkbox).Nth(n) — no Name filter");
        sb.AppendLine("7.  No using statements — GlobalUsings.cs already imports all required namespaces");
        sb.AppendLine("8.  Plain helper class — do NOT add any attributes to the class");
        sb.AppendLine("9.  If EXISTING CODE is provided: output the COMPLETE updated class,");
        sb.AppendLine("    all old methods preserved, new methods appended. Never remove methods.");
        sb.AppendLine("10. Never duplicate a method that already exists.");
        sb.AppendLine();
        sb.Append("Return ONLY valid compilable C# — no markdown fences, no explanation, no comments.");
        return sb.ToString();
    }

    // ── Step Definition Generator ─────────────────────────────────────────────
    public static string StepDefinitionGenerator(
        string pageClassName,
        string registryContext = "",
        IEnumerable<string>? availableMethods = null)
    {
        var stepClassName = pageClassName.Replace("Page", "Steps");
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("You are a Reqnroll C# BDD expert generating or updating a step definitions class.");
        sb.AppendLine($"The Page Object for this class is: {pageClassName}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(registryContext))
        {
            sb.AppendLine(registryContext);
            sb.AppendLine();
        }

        var methods = availableMethods?.ToList();
        if (methods?.Any() == true)
        {
            sb.AppendLine($"AVAILABLE METHODS on {pageClassName}");
            sb.AppendLine("Call ONLY these exact signatures — do NOT invent or rename methods:");
            foreach (var m in methods)
                sb.AppendLine($"  {m}");
            sb.AppendLine();
        }

        sb.AppendLine("RULES");
        sb.AppendLine("1.  Namespace:  Playwright.AiFramework.StepDefinitions");
        sb.AppendLine($"2.  Class name: {stepClassName}");
        sb.AppendLine("3.  Add the [Binding] attribute to the class");
        sb.AppendLine($"4.  Constructor must accept {pageClassName} as a parameter — Reqnroll DI injects it");
        sb.AppendLine("5.  Each [Given/When/Then] regex must match the Gherkin step text EXACTLY");
        sb.AppendLine("6.  Do NOT add a [Scope] attribute");
        sb.AppendLine("7.  No using statements — GlobalUsings.cs already imports all required namespaces");
        sb.AppendLine("8.  If EXISTING CODE is provided: output the COMPLETE updated class,");
        sb.AppendLine("    all old bindings preserved, new ones appended. Never remove bindings.");
        sb.AppendLine("9.  Never duplicate a [Given/When/Then] binding that already exists.");
        sb.AppendLine($"10. Every step body MUST call one of the AVAILABLE METHODS listed above on {pageClassName}.");
        sb.AppendLine();
        sb.Append("Return ONLY valid compilable C# — no markdown fences, no explanation, no comments.");
        return sb.ToString();
    }
}