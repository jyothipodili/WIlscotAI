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

# Patch allure.config.json: replace Windows absolute path with container path
if [ -f /app/allure.config.json ]; then
    jq '.allure.directory = "/app/allure-results"' /app/allure.config.json \
        > /tmp/allure.config.patched.json \
        && mv /tmp/allure.config.patched.json /app/allure.config.json
    echo "  Allure dir  : /app/allure-results (patched)"
fi

echo "======================================================"

# Build dotnet test arguments
TEST_ARGS=(
    "test" "WillscotAutomation.csproj"
    "-c" "Release"
    "--no-build"
    "--logger:trx;LogFileName=/app/TestResults/results.trx"
    "--logger:console;verbosity=normal"
)

# Append NUnit category filter if specified
if [ -n "${FILTER}" ]; then
    TEST_ARGS+=("--filter" "Category=${FILTER}")
    echo "  Applying NUnit filter: Category=${FILTER}"
fi

echo "  Command: dotnet ${TEST_ARGS[*]}"
echo "======================================================"

# Execute tests
dotnet "${TEST_ARGS[@]}"
EXIT_CODE=$?

echo ""
echo "======================================================"
echo "  Test run complete   Exit code : ${EXIT_CODE}"
echo "  TRX results  : /app/TestResults/results.trx"
echo "  Allure data  : /app/allure-results"
echo "  Logs         : /app/logs"
echo "======================================================"

exit ${EXIT_CODE}
