# AI Playwright C# Framework

A BDD test automation framework that uses the **Claude API** to
auto-generate Playwright actions from plain-English Gherkin — no pre-written step
implementations required.

```
┌─────────────────┐    ┌──────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  Login.feature  │ →  │ FeatureFileReader │ →  │ AnthropicClient  │ →  │ PlaywrightRunner │
│  (plain Gherkin)│    │  (extracts steps) │    │  (Claude API)    │    │  (real browser)  │
└─────────────────┘    └──────────────────┘    └──────────────────┘    └─────────────────┘
                                                         │
                                                         ▼
                                               GeneratedActions/
                                               ScenarioTitle.json   ← cached for re-runs
```

---

## Quick Start

### 1 — Prerequisites

```bash
dotnet --version   # 8.0+
```

### 2 — Build and install Playwright browsers

```bash
cd ai-playwright-mcp
dotnet build
# Windows
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
# macOS / Linux
./bin/Debug/net8.0/playwright.sh install chromium
```

### 3 — Add your Anthropic API key

Edit **appsettings.json**:
```json
{ "Anthropic": { "ApiKey": "sk-ant-..." } }
```

Or set an environment variable (takes precedence):
```
ANTHROPIC__APIKEY=sk-ant-...
```

### 4 — Run the tests

```bash
dotnet test
```

On the **first run** for each scenario, the framework:
- Reads the `.feature` file from disk
- Sends the scenario text to Claude
- Receives a JSON action plan and saves it to `GeneratedActions/`
- Executes the actions in a real Chromium browser

On **subsequent runs**, it loads from the cache — no API call, instant execution.

---

## Project Layout

```
ai-playwright-mcp/
├── AI/
│   ├── PlaywrightAction.cs       ← Typed model for a single browser action
│   ├── AnthropicClient.cs        ← Thin HttpClient wrapper for /v1/messages
│   ├── SystemPrompts.cs          ← Engineered prompt (action schemas + site reference)
│   ├── ActionCache.cs            ← JSON disk cache (GeneratedActions/)
│   └── FeatureFileReader.cs      ← Extracts a Scenario block from a .feature file
│
├── Core/
│   ├── PlaywrightContext.cs      ← Shared IPage/IBrowser per scenario (Reqnroll DI)
│   └── PlaywrightRunner.cs       ← Dispatches AI actions → Playwright API calls
│
├── Hooks/
│   ├── BrowserHooks.cs           ← BeforeScenario(Order=1): launch / AfterScenario: teardown
│   └── AiGenerationHook.cs       ← BeforeScenario(Order=10): read → generate → execute
│
├── StepDefinitions/
│   └── AiStepDefinitions.cs      ← [Scope(Tag="ai_generated")] wildcard no-op steps
│
├── Features/
│   ├── Login.feature
│   ├── Checkboxes.feature
│   └── AddRemoveElements.feature
│
├── GeneratedActions/             ← AI output cache (delete .json to force re-generation)
├── Screenshots/                  ← Failure screenshots (captured automatically)
├── AppConfig.cs                  ← Static config via Microsoft.Extensions.Configuration
├── GlobalUsings.cs
├── appsettings.json
├── reqnroll.json
└── Playwright.AiFramework.csproj
```

---

## Supported Action Types

| Action | What it does | Key fields |
|---|---|---|
| `navigate` | `page.GotoAsync(url)` | `url` |
| `fill` | `locator.FillAsync(value)` | `locatorType`, `locatorName`, `value` |
| `click` | `locator.ClickAsync()` | `locatorType`, `locatorName` |
| `check` | `locator.CheckAsync()` | `locatorType`, `locatorName` |
| `uncheck` | `locator.UncheckAsync()` | `locatorType`, `locatorName` |
| `select` | `locator.SelectOptionAsync(value)` | `locatorType`, `locatorName`, `value` |
| `assert_visible` | `Expect(locator).ToBeVisibleAsync()` | `locatorType`, `locatorName` |
| `assert_text` | `Expect(locator).ToContainTextAsync(expected)` | + `expected` |
| `assert_checked` | `Expect(locator).ToBeCheckedAsync()` | `locatorType`, `locatorName` |
| `assert_url` | `Expect(page).ToHaveURLAsync(regex)` | `expected` (regex) |
| `wait_for_url` | `page.WaitForURLAsync(url)` | `url` |

### Locator strategies (priority order)

| `locatorType` | Playwright method |
|---|---|
| `role:button`, `role:heading`, `role:checkbox`, … | `GetByRole(AriaRole.X, Name)` |
| `label` | `GetByLabel(name)` |
| `text` | `GetByText(name)` |
| `placeholder` | `GetByPlaceholder(name)` |
| `testid` | `GetByTestId(name)` |

Add `"index": 0` to any action to append `.Nth(0)` — useful for un-named elements
(e.g. checkboxes without ARIA labels).

---

## Hook Execution Order

```
BeforeScenario(Order=1)   BrowserHooks      → launch Chromium
BeforeScenario(Order=10)  AiGenerationHook  → read feature → call Claude → execute Playwright
  Step: "Given ..."       AiStepDefinitions → no-op (logs ✓, reports step as passed)
  Step: "When ..."        AiStepDefinitions → no-op
  Step: "Then ..."        AiStepDefinitions → no-op
AfterScenario(Order=99)   BrowserHooks      → screenshot on fail → close browser
```

---

## Demo Tips

| Goal | Setting |
|---|---|
| Visible browser | `"Headless": false` (default) |
| Slow-motion demo | `"SlowMo": 500` |
| Force re-generation | Delete `GeneratedActions/*.json` |
| Show AI output | Open `GeneratedActions/Scenario_Name.json` live |
| CI / headless | `"Headless": true` or `BROWSER__HEADLESS=true` |

---

## Extending to New Pages

1. Add a new `.feature` file with `@ai_generated` tag.
2. Write plain-English Gherkin steps — no step definitions needed.
3. Update the **Site Reference** section in `AI/SystemPrompts.cs` with the new page's
   locator hints so Claude produces accurate actions on first generation.
4. Run `dotnet test` — Claude generates and caches the action plan automatically.
