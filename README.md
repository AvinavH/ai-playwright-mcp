# AI Playwright C# Framework

An AI-powered BDD test automation framework using **Playwright**, **Reqnroll** and the **Claude API**.
Write plain-English Gherkin — the framework generates the action plan, executes it in a real browser,
auto-creates Page Objects and Step Definitions, and publishes a rich Allure report.
A fully working Azure DevOps CI pipeline is included out of the box.

---

## How it works

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         @ai_generated scenario                              │
└──────────────────────────────────┬──────────────────────────────────────────┘
                                   │
                    FeatureFileReader (extracts scenario text)
                                   │
                    AnthropicClient → TestActionGenerator prompt
                    (site-aware: knows locators for the AUT)
                                   │
                         JSON Action Plan
                    ┌──────────────┴──────────────┐
                    │                             │
             Cached to disk               PlaywrightRunner
          GeneratedActions/               (executes in Chromium)
          ScenarioName.json               ↓  ↓  ↓  ↓  ↓
                                    navigate fill click assert
                                          │
                         ┌────────────────┴────────────────┐
                         │                                 │
               PageObjectGenerator               StepDefinitionGenerator
               (verified actions as              (proven method signatures
                locator ground truth)             as hard constraint)
                         │                                 │
              Pages/LoginPage.generated.cs    StepDefinitions/LoginSteps.generated.cs
                         │                                 │
                         └────────────┬────────────────────┘
                                      │
                               GRADUATION
                          rename .generated.cs → .cs
                          remove @ai_generated tag
                                      │
                         Reqnroll uses concrete step defs
                         Claude API no longer called for
                         graduated scenarios
```

---

## Quick Start

### 1 — Prerequisites

```bash
dotnet --version    # 8.0+
node --version      # for Allure CLI
```

### 2 — Build and install Playwright browsers

```bash
cd ai-playwright-framework
dotnet build

# Windows
pwsh bin/Debug/net8.0/playwright.ps1 install chromium

# macOS / Linux
./bin/Debug/net8.0/playwright.sh install chromium
```

### 3 — Configure your Anthropic API key

**Option A — `.env` file (recommended)**

Create `.env` at the project root:
```
ANTHROPIC__APIKEY=sk-ant-...
```

**Option B — `appsettings.json`**
```json
{ "Anthropic": { "ApiKey": "sk-ant-..." } }
```

**Option C — Environment variable (CI / shell)**
```bash
# Windows PowerShell
$env:ANTHROPIC__APIKEY = "sk-ant-..."

# Linux / macOS
export ANTHROPIC__APIKEY=sk-ant-...
```

> The double underscore `__` maps to `:` in Microsoft.Extensions.Configuration,
> so `ANTHROPIC__APIKEY` resolves to `Anthropic:ApiKey` in config hierarchy.

### 4 — Run the tests

```bash
dotnet test
```

### 5 — View the Allure report

```bash
# Install Allure CLI once
npm install -g allure-commandline

# Serve live report after test run
allure serve allure-results
```

---

## Project Layout

```
ai-playwright-framework/
│
├── AI/
│   ├── AnthropicClient.cs        ← Thin HttpClient wrapper for /v1/messages
│   ├── PlaywrightAction.cs       ← Typed model for a single browser action
│   ├── SystemPrompts.cs          ← Engineered prompts (site-aware + code gen)
│   ├── ActionCache.cs            ← JSON disk cache under GeneratedActions/
│   ├── FeatureFileReader.cs      ← Extracts a Scenario block from a .feature file
│   ├── CodeGenerator.cs          ← Generates Page Objects + Step Definitions
│   └── RegistryReader.cs         ← Scans Pages/ and StepDefinitions/ for context
│
├── Core/
│   ├── PlaywrightContext.cs      ← Shared IPage/IBrowser per scenario (Reqnroll DI)
│   └── PlaywrightRunner.cs       ← Adaptive locator resolution + action execution
│
├── Hooks/
│   ├── BrowserHooks.cs           ← Launch browser, register IPage with DI, teardown
│   ├── AiGenerationHook.cs       ← Orchestrates: read → generate → execute → code-gen
│   └── AllureGlobalSetup.cs      ← BeforeTestRun: environment panel + failure categories
│
├── Reporting/
│   └── AllureReporter.cs         ← Step wrapping, screenshots, artefact attachments
│
├── StepDefinitions/
│   └── AiStepDefinitions.cs      ← Wildcard no-ops scoped to @ai_generated scenarios
│
├── Features/
│   ├── Login.feature
│   ├── Checkboxes.feature
│   └── AddRemoveElements.feature
│
├── Pages/                        ← AI-generated Page Objects land here
│   └── (LoginPage.generated.cs, etc.)
│
├── GeneratedActions/             ← Action plan cache — delete .json to force re-gen
├── Screenshots/                  ← Failure screenshots (auto-captured)
├── allure-results/               ← Allure raw data (git-ignored)
├── allure-report/                ← Allure HTML report (git-ignored)
│
├── AppConfig.cs
├── GlobalUsings.cs
├── appsettings.json
├── allureConfig.json             ← Allure output directory config
├── allure-categories.json        ← Failure triage category patterns
├── azure-pipelines.yml           ← Azure DevOps CI pipeline
├── reqnroll.json
└── Playwright.AiFramework.csproj
```

---

## The Graduation Workflow

Graduation is the process of moving from AI-driven execution to concrete,
developer-owned step definitions. It is a one-time action per feature.

### Phase 1 — AI generation (first run)

Tag your feature file at the **Feature** level:

```gherkin
@ai_generated
Feature: Login Page

  Scenario: Successful login with valid credentials
    Given I am on the login page
    When I enter the username "tomsmith" and password "SuperSecretPassword!"
    And I click the Login button
    Then I should be redirected to the secure area
```

Run `dotnet test`. The framework:
1. Reads the scenario from the `.feature` file
2. Calls Claude with the site-aware `TestActionGenerator` prompt
3. Executes the returned JSON action plan in Chromium
4. Calls Claude again with the **verified** action plan as locator ground truth
5. Writes `Pages/LoginPage.generated.cs` and `StepDefinitions/LoginSteps.generated.cs`

### Phase 2 — Review the generated code

Open the generated files and check:

```csharp
// Pages/LoginPage.generated.cs — verify locators match the actual page
public async Task IShouldSeeASuccessFlashMessage()
{
    // Should be GetByText(), NOT GetByRole(AriaRole.Alert)
    // for sites that use plain <div class="flash"> elements
    await Assertions.Expect(
        _page.GetByText("You logged into a secure area!")).ToBeVisibleAsync();
}
```

Fix any incorrect locators directly in the `.generated.cs` file before graduating.

### Phase 3 — Graduate (rename + untag)

```bash
# Rename — remove the .generated. infix
mv Pages/LoginPage.generated.cs           Pages/LoginPage.cs
mv StepDefinitions/LoginSteps.generated.cs StepDefinitions/LoginSteps.cs
```

Then in the feature file, **remove `@ai_generated` from the Feature level**:

```gherkin
Feature: Login Page   ← tag removed

  Scenario: Successful login with valid credentials
    Given I am on the login page
    ...
```

Run `dotnet test` — Reqnroll now binds directly to `LoginSteps.cs`. No Claude API call, no hook, instant execution.

> **Rule:** Never edit a `.generated.cs` file directly — the next AI generation
> overwrites it. Graduate (rename) first, then edit freely.

### Phase 4 — Adding new scenarios to a graduated feature

Add `@ai_generated` at the **scenario level** (not feature level):

```gherkin
Feature: Login Page   ← no tag here

  Scenario: Successful login
    Given I am on the login page
    ...

  @ai_generated          ← only this new scenario uses AI
  Scenario: Login with empty credentials
    Given I am on the login page
    When I click the Login button without entering credentials
    Then I should see a validation error
```

The framework:
- Detects the new scenario (cache miss)
- Generates the action plan and executes it
- Finds `LoginPage.cs` (the graduated file) and **appends** new methods to it
- Finds `LoginSteps.cs` and appends new step bindings
- Never touches or overwrites existing methods

### Graduation outcome matrix

| File state | CodeGenerator behaviour |
|---|---|
| Neither exists | Creates `Name.generated.cs` |
| `.generated.cs` only | Updates `.generated.cs` |
| `.cs` only (graduated) | Updates `.cs` in place |
| Both (should not happen) | Updates `.cs` (found first) |

---

## Self-Healing Retries

When a test fails, the framework automatically asks Claude to fix the action plan
and retries — up to `MaxRetries` times (default: 2).

```
Attempt 1  →  run initial plan  →  FAIL
             send (failed plan + Playwright error) to Claude
             Claude returns corrected plan
             cache overwritten with healed plan
             page reset (cookies cleared, navigate to about:blank)

Attempt 2  →  run healed plan   →  PASS / FAIL
             if PASS → done, healed plan cached
             if FAIL → repeat...

Attempt 3  →  FAIL → graceful fail
             Page Object + Step Defs still generated
             Allure report shows each retry as a named step
             💾 "Artefacts saved for manual review"
```

Code artefacts (Page Objects, Step Definitions) are **always written to disk**
even when all retries are exhausted, so you can review and fix them manually.

To adjust the retry count, change `MaxRetries` in `Hooks/AiGenerationHook.cs`.

---

## Allure Reporting

Every pipeline run and local test run produces a rich Allure report.

### What's captured per test

| Report element | Content |
|---|---|
| Named steps | Every Playwright action (navigate, fill, click, assert) with timing |
| Screenshots | Captured after `navigate` and `assert_*` actions |
| Failure screenshot | Full-page screenshot on test failure |
| AI Action Plan | Initial JSON plan attached as `AI Action Plan.json` |
| Healed plans | Each self-heal retry attaches a `Healed Action Plan (retry N).json` |
| Generated code | `LoginPage.cs` and `LoginSteps.cs` attached for review |
| Error details | Full failure message attached as text |

### Screenshot modes

Control how many screenshots are captured via `appsettings.json`:

```json
"Allure": {
  "ScreenshotMode": "KeyActions"
}
```

| Mode | Screenshots taken |
|---|---|
| `KeyActions` (default) | After `navigate` and `assert_*` only |
| `All` | After every single action |
| `None` | No inline screenshots (failure screenshot still captured) |

### Viewing reports

```bash
# Live interactive server (auto-opens browser)
allure serve allure-results

# Generate static HTML to share or deploy
allure generate allure-results -o allure-report --clean
allure open allure-report
```

### Failure categories

Failures are automatically grouped in the Categories view:

| Category | Pattern matched |
|---|---|
| AI Generation Failures | Anthropic API errors, invalid JSON responses |
| Element Not Found | Playwright timeout / waiting for locator |
| Strict Mode Violation | Multiple elements matched a single locator |
| Assertion Failures | ToBeVisible / ToContainText failures |
| Self-Healing Exhausted | All retries consumed |
| API Key Not Configured | Missing `ANTHROPIC__APIKEY` |
| Navigation / Timeout | Network errors, navigation timeouts |

---

## Supported Action Types

| Action | Playwright call | Key fields |
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
| `assert_url` | `Expect(page).ToHaveURLAsync(regex)` | `expected` |
| `wait_for_url` | `page.WaitForURLAsync(url)` | `url` |

### Locator strategy (adaptive, Playwright best practices)

| `locatorType` | Playwright method | Notes |
|---|---|---|
| `role:button`, `role:heading`, `role:checkbox` … | `GetByRole(AriaRole.X)` | Tries exact match first, falls back to partial if 0 results |
| `label` | `GetByLabel(name)` | Preferred for form inputs with visible labels |
| `text` | `GetByText(name)` | Partial match by default — avoids strict-mode violations |
| `placeholder` | `GetByPlaceholder(name)` | |
| `testid` | `GetByTestId(name)` | |

Optional fields on any action:

```json
"index": 0      // appends .Nth(0) — required for elements without accessible names
"exact": true   // force exact text/name matching
```

---

## Hook Execution Order

```
BeforeTestRun                AllureGlobalSetup  → write environment.properties
                                                → copy categories.json

BeforeScenario (Order=1)     BrowserHooks       → launch Chromium
                                                → register IPage with Reqnroll DI
BeforeScenario (Order=10)    AiGenerationHook   → read feature file
  [@ai_generated only]                          → check action cache
                                                → call Claude (if cache miss)
                                                → execute Playwright actions
                                                → self-heal on failure (up to N retries)
                                                → generate Page Object + Step Defs
                                                  (only on cache miss or failure)

  Step: "Given ..."          AiStepDefinitions  → no-op (logs ✓)
  Step: "When ..."           AiStepDefinitions  → no-op
  Step: "Then ..."           AiStepDefinitions  → no-op

  [Graduated scenarios]      ConcreteStepClass  → calls LoginPage methods directly

AfterScenario (Order=99)     BrowserHooks       → attach failure screenshot to Allure
                                                → close browser
```

---

## Azure DevOps CI Pipeline

A production-ready `azure-pipelines.yml` is included at the project root.

### Pipeline steps

| Step | Purpose |
|---|---|
| Use .NET 8 | Ensures correct SDK version |
| dotnet restore + build | Compile the project |
| Install Playwright Chromium | Downloads portable PowerShell 7, runs `playwright.ps1` |
| dotnet test | Runs all tests headless, injects secrets from variable group |
| Publish test results | Shows pass/fail in the Azure DevOps Tests tab |
| Install Allure CLI + Generate report | Produces the HTML report |
| Publish artefacts | `allure-results` and `allure-report` downloadable from the run |

### Setup requirements

1. **Self-hosted Windows agent** registered in the `Default` pool
2. **Variable group** `ai-playwright-secrets` containing `ANTHROPIC__APIKEY` (secret)
3. **Node.js** installed on the agent (for `allure-commandline`)

### CI overrides

The pipeline sets these environment variables to override `appsettings.json`:

```yaml
BROWSER__HEADLESS: 'true'    # no visible browser in CI
BROWSER__SLOWMO:   '0'       # no artificial delay
ANTHROPIC__APIKEY: $(ANTHROPIC__APIKEY)  # from variable group
```

### Committing the action cache

The `GeneratedActions/*.json` files should be committed to the repo so CI
reuses cached action plans rather than calling the Claude API on every run:

```bash
# Remove from .gitignore if present, then:
git add GeneratedActions/*.json
git commit -m "chore: commit action cache for CI"
```

For graduated scenarios (no `@ai_generated` tag), the Claude API is never
called at all — Reqnroll uses the concrete step definitions directly.

---

## Configuration Reference

All settings live in `appsettings.json` and can be overridden via environment
variables using the `__` separator (e.g. `BROWSER__HEADLESS=true`).

```json
{
  "Anthropic": {
    "ApiKey": "YOUR_API_KEY_HERE"
  },
  "Browser": {
    "Headless": false,
    "SlowMo": 50,
    "BaseUrl": "https://the-internet.herokuapp.com"
  },
  "Allure": {
    "ScreenshotMode": "KeyActions"
  }
}
```

---

## Demo Tips

| Goal | Setting / Action |
|---|---|
| Visible browser (default) | `"Headless": false` |
| Slow-motion walkthrough | `"SlowMo": 500` |
| Force action re-generation | Delete `GeneratedActions/ScenarioName.json` |
| Force code re-generation | Delete the `.generated.cs` files + cache file |
| Show AI output live | Open `GeneratedActions/ScenarioName.json` during demo |
| Show generated code | Open `Pages/LoginPage.generated.cs` after first run |
| CI headless mode | `BROWSER__HEADLESS=true` env var |
| View Allure report | `allure serve allure-results` |

---

## Extending to New Pages

1. Add a new `.feature` file with `@ai_generated` at the Feature or Scenario level
2. Write plain-English Gherkin — no step definitions needed
3. Add the new page's URL and key locators to the **Site Reference** section
   in `AI/SystemPrompts.cs` (both `TestActionGenerator` and `PageObjectGenerator`)
   so Claude produces accurate locators on first generation
4. Run `dotnet test` — actions are generated, executed, and code is produced
5. Review `Pages/NewPage.generated.cs` for locator accuracy
6. Graduate when satisfied: rename to `.cs` and remove the `@ai_generated` tag