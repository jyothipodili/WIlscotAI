# =============================================================
#  WillScot Automation Framework — Docker Image
#  Stack : .NET 8 | Playwright 1.44 | Reqnroll 2.2 | NUnit 4.1
#  Base  : mcr.microsoft.com/playwright/dotnet:v1.44.0-jammy
#  Ubuntu 22.04 (Jammy) — includes all Playwright browser deps
# =============================================================

FROM mcr.microsoft.com/playwright/dotnet:v1.44.0-jammy

USER root
WORKDIR /app

# ── system utilities ──────────────────────────────────────────
# jq: JSON patching for allure.config.json at runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends jq \
    && rm -rf /var/lib/apt/lists/*

# ── NuGet restore (separate layer — cached unless .csproj changes) ──
COPY WillscotAutomation/WillscotAutomation.csproj ./
RUN dotnet restore WillscotAutomation.csproj

# ── full source copy ──────────────────────────────────────────
COPY WillscotAutomation/ ./

# ── build (Release) ───────────────────────────────────────────
# PatchReqnrollTearDown target runs automatically before CoreCompile
RUN dotnet build WillscotAutomation.csproj -c Release --no-restore

# ── output directories (bind-mounted to host at runtime) ──────
RUN mkdir -p /app/allure-results /app/logs /app/TestResults

# ── entrypoint script ─────────────────────────────────────────
# Strip Windows CRLF line endings (dos2unix equivalent) before chmod
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN sed -i 's/\r$//' /docker-entrypoint.sh && chmod +x /docker-entrypoint.sh

# ── default environment variables (overridden via --env-file) ─
ENV TEST_ENV=QA \
    BROWSER=chromium \
    HEADLESS=true \
    FILTER="" \
    TEAMS_WEBHOOK_URL="" \
    ZEPHYR_JWT_TOKEN="" \
    JIRA_API_TOKEN=""

ENTRYPOINT ["/docker-entrypoint.sh"]
