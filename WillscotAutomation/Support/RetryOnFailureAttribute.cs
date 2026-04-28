using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace WillscotAutomation.Support;

/// <summary>
/// Retries a failed test up to <paramref name="retryCount"/> additional times.
/// Works at class level (applies to every test method in the fixture) unlike
/// NUnit 4's built-in [Retry] which is method-only.
///
/// Each retry runs the full NUnit SetUp → test body → TearDown cycle so
/// Reqnroll's BeforeScenario/AfterScenario hooks fire fresh for every attempt,
/// giving each retry a brand-new Playwright browser context.
///
/// Usage: [RetryOnFailure(2)] → up to 3 total attempts (1 original + 2 retries).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RetryOnFailureAttribute : NUnitAttribute, IRepeatTest
{
    private readonly int _retryCount;

    public RetryOnFailureAttribute(int retryCount)
    {
        if (retryCount < 1)
            throw new ArgumentOutOfRangeException(nameof(retryCount),
                "Retry count must be at least 1.");
        _retryCount = retryCount;
    }

    public TestCommand Wrap(TestCommand command) =>
        new RetryCommand(command, _retryCount);

    // ── Inner retry command ────────────────────────────────────────────────────

    private sealed class RetryCommand : DelegatingTestCommand
    {
        private readonly int _retryCount;

        public RetryCommand(TestCommand innerCommand, int retryCount)
            : base(innerCommand)
        {
            _retryCount = retryCount;
        }

        public override TestResult Execute(TestExecutionContext context)
        {
            var attemptsLeft = _retryCount;

            while (true)
            {
                try
                {
                    context.CurrentResult = innerCommand.Execute(context);
                }
                catch (Exception ex)
                {
                    // Unhandled exception — record it as a test error
                    context.CurrentResult ??= context.CurrentTest.MakeTestResult();
                    context.CurrentResult.RecordException(ex);
                }

                var state = context.CurrentResult.ResultState;

                // Only retry on Failure or Error — pass/skip/inconclusive are final
                bool shouldRetry = (state == ResultState.Failure ||
                                    state == ResultState.Error)
                                   && attemptsLeft-- > 0;

                if (!shouldRetry) break;

                // Reset result so the next attempt starts clean
                context.CurrentResult = context.CurrentTest.MakeTestResult();
            }

            return context.CurrentResult;
        }
    }
}
