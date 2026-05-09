#!/bin/bash
# =============================================================
#  WillScot Automation — Container Entrypoint
#  Patches allure config for Linux paths, then runs dotnet test
# =============================================================
set -e

echo "======================================================"
echo "  WillScot Automation Test Runner"
echo "  Environment : ${TEST_ENV}"
echo "  Browser     : ${BROWSER}"
echo "  Headless    : ${HEADLESS}"
echo "  Filter      : ${FILTER:-<none — all tests>}"
echo "======================================================"

# Patch allure.config.json in both the project root and the build output dir
for CONFIG in /app/allure.config.json /app/bin/Release/net8.0/allure.config.json; do
    if [ -f "$CONFIG" ]; then
        jq '.allure.directory = "/app/allure-results"' "$CONFIG" \
            > /tmp/allure.config.patched.json \
            && mv /tmp/allure.config.patched.json "$CONFIG"
    fi
done
echo "  Allure dir  : /app/allure-results (patched)"

echo "======================================================"

# Start virtual display when running headed (no physical monitor in container)
if [ "${HEADLESS}" = "false" ]; then
    Xvfb :99 -screen 0 1920x1080x24 -ac +extension GLX &
    export DISPLAY=:99
    sleep 1
    echo "  Virtual display started on :99"
fi

# Number of parallel NUnit workers (default 4; override via WORKERS env var)
WORKERS="${WORKERS:-4}"

# Ensure output directories exist
mkdir -p /app/allure-results /app/TestResults /app/logs /app/videos /app/traces

# Build dotnet test arguments
TEST_ARGS=(
    "test" "WillscotAutomation.csproj"
    "-c" "Release"
    "--no-build"
    "--logger:trx;LogFileName=/app/TestResults/results.trx"
    "--logger:console;verbosity=normal"
    "--"
    "NUnit.NumberOfTestWorkers=${WORKERS}"
)

# Append NUnit category filter if specified
if [ -n "${FILTER}" ]; then
    TEST_ARGS+=("NUnit.Where=cat==${FILTER}")
    echo "  Applying NUnit filter: cat==${FILTER}"
fi

echo "  Workers     : ${WORKERS}"
echo "  Command: dotnet ${TEST_ARGS[*]}"
echo "======================================================"

# Execute tests
dotnet "${TEST_ARGS[@]}"
EXIT_CODE=$?

# Move any remaining loose video files into allure-results so kubectl cp
# only needs to pull one directory
if [ -d /app/videos ]; then
    find /app/videos -name "*.webm" -exec mv -t /app/allure-results {} + 2>/dev/null || true
fi

echo ""
echo "======================================================"
echo "  Test run complete   Exit code : ${EXIT_CODE}"
echo "  TRX results  : /app/TestResults/results.trx"
echo "  Allure data  : /app/allure-results"
echo "  Traces       : /app/TestResults/traces"
echo "  Logs         : /app/logs"
echo "======================================================"

exit ${EXIT_CODE}
