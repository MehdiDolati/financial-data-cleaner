using System;
using Validator.Application.Web;

namespace Validator.Application.Tests.Web;

// Lifecycle state machine tests for WebRunStatus. Every allowed transition
// in contracts/web-run-lifecycle.md must be accepted, every forbidden one
// rejected rather than coerced (FR-008, SC-003, SC-007).
public class WebRunStatusTests
{
    public static TheoryData<WebRunStatus, WebRunStatus> AllowedTransitions => new()
    {
        { WebRunStatus.Pending, WebRunStatus.Running },
        { WebRunStatus.Pending, WebRunStatus.Failed },
        { WebRunStatus.Running, WebRunStatus.CompletedClean },
        { WebRunStatus.Running, WebRunStatus.CompletedWithFindings },
        { WebRunStatus.Running, WebRunStatus.Failed },
        { WebRunStatus.Failed, WebRunStatus.Pending }
    };

    public static TheoryData<WebRunStatus, WebRunStatus> ForbiddenTransitions => new()
    {
        { WebRunStatus.Pending, WebRunStatus.CompletedClean },
        { WebRunStatus.Pending, WebRunStatus.CompletedWithFindings },
        { WebRunStatus.Running, WebRunStatus.Pending },
        { WebRunStatus.Failed, WebRunStatus.Running },
        { WebRunStatus.Failed, WebRunStatus.CompletedClean },
        { WebRunStatus.Failed, WebRunStatus.CompletedWithFindings },
        { WebRunStatus.CompletedClean, WebRunStatus.Pending },
        { WebRunStatus.CompletedClean, WebRunStatus.Running },
        { WebRunStatus.CompletedClean, WebRunStatus.CompletedWithFindings },
        { WebRunStatus.CompletedClean, WebRunStatus.Failed },
        { WebRunStatus.CompletedWithFindings, WebRunStatus.Pending },
        { WebRunStatus.CompletedWithFindings, WebRunStatus.Running },
        { WebRunStatus.CompletedWithFindings, WebRunStatus.CompletedClean },
        { WebRunStatus.CompletedWithFindings, WebRunStatus.Failed }
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void Allowed_transition_is_accepted(WebRunStatus from, WebRunStatus to)
    {
        var action = () => WebRunStatusGuard.EnsureTransition(from, to);

        action.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(ForbiddenTransitions))]
    public void Forbidden_transition_is_rejected(WebRunStatus from, WebRunStatus to)
    {
        var action = () => WebRunStatusGuard.EnsureTransition(from, to);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{from}*{to}*");
    }

    [Fact]
    public void Same_state_transition_is_rejected_for_every_state()
    {
        foreach (var status in Enum.GetValues<WebRunStatus>())
        {
            var action = () => WebRunStatusGuard.EnsureTransition(status, status);

            action.Should().Throw<InvalidOperationException>(
                $"a run may never transition {status} -> {status}");
        }
    }

    [Fact]
    public void Only_pending_and_running_are_non_terminal()
    {
        WebRunStatusGuard.IsTerminal(WebRunStatus.Pending).Should().BeFalse();
        WebRunStatusGuard.IsTerminal(WebRunStatus.Running).Should().BeFalse();
        WebRunStatusGuard.IsTerminal(WebRunStatus.CompletedClean).Should().BeTrue();
        WebRunStatusGuard.IsTerminal(WebRunStatus.CompletedWithFindings).Should().BeTrue();
        WebRunStatusGuard.IsTerminal(WebRunStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void Only_completed_clean_reads_as_clean()
    {
        WebRunStatusGuard.ReadsAsClean(WebRunStatus.Pending).Should().BeFalse();
        WebRunStatusGuard.ReadsAsClean(WebRunStatus.Running).Should().BeFalse();
        WebRunStatusGuard.ReadsAsClean(WebRunStatus.CompletedWithFindings).Should().BeFalse();
        WebRunStatusGuard.ReadsAsClean(WebRunStatus.Failed).Should().BeFalse();
        WebRunStatusGuard.ReadsAsClean(WebRunStatus.CompletedClean).Should().BeTrue();
    }

    [Fact]
    public void CompletedClean_is_reachable_only_through_running()
    {
        // The only accepted path into CompletedClean is from Running; a run
        // that never executed (Pending) or failed (Failed) can never be clean.
        WebRunStatusGuard.EnsureTransition(WebRunStatus.Running, WebRunStatus.CompletedClean);
        FluentActions.Invoking(() =>
                WebRunStatusGuard.EnsureTransition(WebRunStatus.Pending, WebRunStatus.CompletedClean))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() =>
                WebRunStatusGuard.EnsureTransition(WebRunStatus.Failed, WebRunStatus.CompletedClean))
            .Should().Throw<InvalidOperationException>();
    }
}