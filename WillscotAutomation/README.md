# WillscotAutomation — BDD Playwright Test Framework

Enterprise-grade BDD automation suite for [WillScot](https://www.willscot.com/en) built with **C# / Playwright / Reqnroll / NUnit / Allure**.

---

## Technology Stack

| Layer            | Technology                       |
|------------------|----------------------------------|
| Language         | C# 12 / .NET 8                   |
| Browser Automation | Microsoft Playwright 1.44      |
| BDD Framework    | Reqnroll 2.2 (SpecFlow successor)|
| Test Runner      | NUnit 4                          |
| Design Pattern   | Page Object Model + BDD Layer    |
| Reporting        | Allure Report 2.27               |
| Logging          | Serilog (Console + File sink)    |
| CI/CD            | GitHub Actions                   |
| Configuration    | appsettings.json (env-aware)     |

---

## Folder Structure

```
WillscotAutomation/
├── .github/workflows/          ← GitHub Actions pipeline
├── Config/                     ← appsettings.{env}.json + ConfigReader.cs
├── Drivers/                    ← PlaywrightDriver.cs + PlaywrightContext.cs
├── Features/                   ← WillscotHomepage.feature (BDD scenarios)
├── Hooks/                      ← Reqnroll BeforeScenario / AfterScenario hooks
├── PageObjects/
│   ├── Components/             ← HeaderComponent.cs + NavigationMenu.cs
│   ├── Sections/               ← ProductOfferingsSection.cs + IndustrySolutionsSection.cs
│   └── HomePage.cs             ← Root page object
├── StepDefinitions/            ← HomepageSteps / NavigationSteps / Products / Industry
├── Support/                    ← AssemblyInfo.cs (parallel execution attributes)
├── Utilities/                  ← WaitHelper / HttpHelper / LogCollector / ScreenshotHelper / AllureHelper
├── allure.config.json
├── reqnroll.json
└── WillscotAutomation.csproj
```

---

## Prerequisites

| Tool             | Version  | Install                                    |
|------------------|----------|--------------------------------------------|
| .NET SDK         | 8.0+     | https://dotnet.microsoft.com/download      |
| PowerShell       | 7+       | `winget install Microsoft.PowerShell`      |
| Allure CLI       | 2.27+    | `npm install -g allure-commandline`        |
| Node.js (npm)    | 18+      | https://nodejs.org                         |

---

## Setup

### 1. Clone & Restore

```bash
git clone <repo-url>
cd WillscotAutomation
dotnet restore
```

### 2. Install Playwright Browser Binaries

After a `dotnet build`, Playwright ships a PowerShell helper script:

```bash
dotnet build

# Install the default Chromium browser
pwsh bin/Debug/net8.0/playwright.ps1 install chromium

# Or install all browsers at once
pwsh bin/Debug/net8.0/playwright.ps1 install
```

On Linux (CI/CD) also install OS dependencies:

```bash
pwsh bin/Debug/net8.0/playwright.ps1 install --with-deps chromium
```

---

## Running Tests

### Run all tests

```bash
dotnet test
```

### Run with specific environment

```bash
TEST_ENV=QA    dotnet test
TEST_ENV=Stage dotnet test
TEST_ENV=Prod  dotnet test
```

### Run with specific browser

```bash
BROWSER=chromium dotnet test
BROWSER=firefox  dotnet test
BROWSER=webkit   dotnet test
```

### Run in non-headless mode (visible browser)

```bash
HEADLESS=false dotnet test
```

### Run by tag (NUnit Category filter)

```bash
# Smoke tests only
dotnet test --filter "Category=smoke"

# Regression suite
dotnet test --filter "Category=regression"

# Single test case
dotnet test --filter "Category=TC001"
```

### Run specific scenario by name

```bash
dotnet test --filter "Name~TC-001"
```

### Combined example

```bash
TEST_ENV=QA BROWSER=chromium HEADLESS=true dotnet test --filter "Category=smoke"
```

---

## Allure Reporting

### 1. Run tests (results written to `./allure-results`)

```bash
dotnet test
```

### 2. Generate HTML report

```bash
allure generate ./allure-results --clean -o ./allure-report
```

### 3. Open report in browser

```bash
allure open ./allure-report
```

### 4. Serve report on local port (auto-opens browser)

```bash
allure serve ./allure-results
```

---

## Parallel Execution

The framework is configured for **scenario-level parallelism** (4 threads by default):

```csharp
// Support/AssemblyInfo.cs
[assembly: Parallelizable(ParallelScope.All)]
[assembly: LevelOfParallelism(4)]
```

Each scenario gets its own isolated `IBrowser` + `IBrowserContext` + `IPage` instance, ensuring full thread safety.

Adjust `LevelOfParallelism` or set `TestSettings.MaxParallelism` in `appsettings.json` to tune for your CI agent.

---

## Configuration Reference

| Key                     | Default                              | Description                      |
|-------------------------|--------------------------------------|----------------------------------|
| `TestSettings:BaseUrl`  | https://www.willscot.com/en          | Site under test                  |
| `TestSettings:Browser`  | chromium                             | Browser engine                   |
| `TestSettings:Headless` | true                                 | Headless mode                    |
| `TestSettings:DefaultTimeout`    | 30000 ms              | Element wait timeout             |
| `TestSettings:NavigationTimeout` | 60000 ms              | Page navigation timeout          |
| `TestSettings:SlowMo`   | 0 ms                                 | Slow-motion delay between actions|
| `TestSettings:RetryCount` | 1                                  | Retry count for flaky tests      |

Override any key via environment variable (env var takes precedence):

```bash
TEST_ENV=QA BROWSER=firefox HEADLESS=false dotnet test
```

---

## Logging

Test logs are written to `./logs/test-YYYYMMDD.log` and streamed to the console.
Log level is controlled by `TestSettings:LogLevel` in `appsettings.json`.

---

## Hooks Reference

| Hook              | Behaviour                                                               |
|-------------------|-------------------------------------------------------------------------|
| `[BeforeTestRun]` | Configures Serilog logger                                               |
| `[BeforeScenario]`| Launches browser, creates context + page, registers in Reqnroll DI     |
| `[AfterScenario]` | On failure: attaches screenshot, console errors, network failures, HTML |
| `[AfterScenario]` | Always: disposes browser / context / page                               |
| `[AfterTestRun]`  | Flushes Serilog                                                         |

---

## Allure Attachments on Failure

| Attachment              | MIME Type    |
|-------------------------|--------------|
| Screenshot (PNG)        | image/png    |
| Browser Console Errors  | text/plain   |
| JS Exceptions           | text/plain   |
| Network Failed Requests | text/plain   |
| Page HTML Dump          | text/html    |

---

## CI/CD (GitHub Actions)

The pipeline (`.github/workflows/playwright-tests.yml`) supports:

- **Push** to `main` / `develop` — runs full suite automatically
- **Pull Requests** — runs full suite on PR
- **Manual trigger** — choose environment, browser, and tag filter

Artefacts retained per run:
- `allure-report-{run}` (30 days)
- `trx-results-{run}` (14 days)
- `allure-results-{run}` (14 days)
- `test-logs-{run}` (14 days)

---

## Troubleshooting

| Problem                         | Fix                                                       |
|---------------------------------|-----------------------------------------------------------|
| `playwright.ps1` not found      | Run `dotnet build` first                                  |
| Browser not installed           | `pwsh bin/Debug/net8.0/playwright.ps1 install chromium`   |
| Tests time out                  | Increase `NavigationTimeout` in `appsettings.QA.json`     |
| Allure results empty            | Ensure `allure.config.json` is copied to output directory |
| Selectors fail on live site     | Inspect updated DOM and adjust locators in Page Objects   |
