<#
.SYNOPSIS
    Run WillScot automation tests: parallel, retry, Allure report, Teams + Email notifications.

.PARAMETER Env
    QA | Stage | Prod  (default: QA)

.PARAMETER Browser
    chromium | firefox | webkit  (default: chromium)

.PARAMETER UIMode
    $true = visible browser  $false = headless CI mode  (default: $true)

.PARAMETER Tags
    NUnit category filter e.g. "smoke". Omit to run all tests.

.PARAMETER OpenReport
    Open Allure HTML report in browser after run (default: $true)

.PARAMETER TeamsWebhook
    Override Teams webhook URL at runtime (optional — config file value used if omitted)

.EXAMPLE
    .\RunTests.ps1
.EXAMPLE
    .\RunTests.ps1 -UIMode $false -Tags smoke
#>
param(
    [string] $Env             = "QA",
    [string] $Browser         = "chromium",
    [bool]   $UIMode          = $true,
    [string] $Tags            = "",
    [bool]   $OpenReport      = $true,
    [string] $TeamsWebhook    = ""
)

$ErrorActionPreference = "Continue"

# ── Paths ─────────────────────────────────────────────────────────────────────
$projectDir    = Join-Path $PSScriptRoot "WillscotAutomation"
$csproj        = Join-Path $projectDir   "WillscotAutomation.csproj"
$runsettings   = Join-Path $projectDir   "WillscotAutomation.runsettings"
$resultsDir    = Join-Path $projectDir   "allure-results"
$reportDir     = Join-Path $projectDir   "allure-report"
$reportHistory = Join-Path $reportDir    "history"
$testOutputDir = Join-Path $projectDir   "TestResults"

# ── Environment variables ─────────────────────────────────────────────────────
$env:TEST_ENV = $Env
$env:BROWSER  = $Browser

if ($UIMode) {
    $env:HEADLESS = "false"
} else {
    $env:HEADLESS = "true"
}

if ($TeamsWebhook) {
    $env:TEAMS_WEBHOOK_URL = $TeamsWebhook
}

# ── Resolve display strings for banner ────────────────────────────────────────
if ($UIMode) {
    $uiLabel = "ON  (visible browser)"
} else {
    $uiLabel = "OFF (headless)"
}

if ($Tags) {
    $tagLabel = $Tags
} else {
    $tagLabel = "all"
}

$qaConfig = Join-Path $projectDir "Config\appsettings.QA.json"
$configJson = Get-Content $qaConfig -Raw
if ($configJson -match '"WebhookUrl"\s*:\s*"[^"]{10}') {
    $teamsLabel = "enabled"
} else {
    $teamsLabel = "disabled - add WebhookUrl to appsettings.QA.json"
}

if ($configJson -match '"EnableEmail"\s*:\s*true') {
    $emailLabel = "enabled"
} else {
    $emailLabel = "disabled"
}

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  WillScot Automation Test Runner" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Env       : $Env"
Write-Host "  Browser   : $Browser"
Write-Host "  UI Mode   : $uiLabel"
Write-Host "  Tags      : $tagLabel"
Write-Host "  Parallel  : 4 workers"
Write-Host "  Retry     : 1 retry per failure (2 total attempts)"
Write-Host "  Email     : $emailLabel"
Write-Host "  Teams     : $teamsLabel"
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# ── Build ─────────────────────────────────────────────────────────────────────
Write-Host "[1/3] Building project..." -ForegroundColor Yellow
& dotnet build $csproj --configuration Debug --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "      Build FAILED. Aborting." -ForegroundColor Red
    exit 1
}
Write-Host "      Build OK" -ForegroundColor Green

# ── Preserve Allure history for trend charts ──────────────────────────────────
if ((Test-Path $reportHistory) -and (Test-Path $resultsDir)) {
    $historyDest = Join-Path $resultsDir "history"
    Copy-Item -Path $reportHistory -Destination $historyDest -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Run tests ─────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "[2/3] Running tests..." -ForegroundColor Yellow
Write-Host ""

$testArgs = @(
    "test", $csproj,
    "--settings",          $runsettings,
    "--results-directory", $testOutputDir,
    "--logger",            "trx;LogFileName=TestResult.trx",
    "--no-build"
)
if ($Tags) {
    $testArgs += @("--filter", "Category=$Tags")
}

& dotnet @testArgs
$testExitCode = $LASTEXITCODE

# ── Generate Allure report ────────────────────────────────────────────────────
Write-Host ""
Write-Host "[3/3] Generating Allure report..." -ForegroundColor Yellow

$allureCmd = Get-Command allure -ErrorAction SilentlyContinue

if ($null -eq $allureCmd) {
    Write-Host ""
    Write-Host "  Allure CLI not found. Install with:" -ForegroundColor Yellow
    Write-Host "    npm install -g allure-commandline" -ForegroundColor White
    Write-Host "    OR: scoop install allure" -ForegroundColor White
    Write-Host "    OR: choco install allure" -ForegroundColor White
    Write-Host ""
    Write-Host "  Raw results: $resultsDir" -ForegroundColor Cyan
} elseif (Test-Path $resultsDir) {
    & allure generate $resultsDir --clean -o $reportDir
    Write-Host "      Report : $reportDir" -ForegroundColor Green

    if ($OpenReport) {
        Write-Host "      Opening in browser..." -ForegroundColor Green
        & allure open $reportDir
    } else {
        Write-Host "      To open: allure open '$reportDir'" -ForegroundColor Cyan
    }
} else {
    Write-Host "  No allure-results found." -ForegroundColor Red
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
if ($testExitCode -eq 0) {
    Write-Host "  Result : ALL TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "  Result : SOME TESTS FAILED (exit $testExitCode)" -ForegroundColor Red
    Write-Host "  Logs   : $projectDir\logs\" -ForegroundColor Yellow
    Write-Host "  TRX    : $testOutputDir\TestResult.trx" -ForegroundColor Yellow
}
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
exit $testExitCode
