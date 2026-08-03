using LSTY.SevenDPanel.Domain.Jobs;
using System.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Domain.Jobs
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Domain")]
    public sealed class JobStateMachineTests
    {
        [Theory]
        [InlineData(JobStatus.Queued, JobStatus.Running)]
        [InlineData(JobStatus.Queued, JobStatus.Cancelled)]
        [InlineData(JobStatus.Running, JobStatus.Succeeded)]
        [InlineData(JobStatus.Running, JobStatus.Failed)]
        [InlineData(JobStatus.Running, JobStatus.Interrupted)]
        [InlineData(JobStatus.Running, JobStatus.ResultUnknown)]
        public void Ordinary_jobs_allow_only_the_fixed_lifecycle_transitions(
            JobStatus current,
            JobStatus next)
        {
            Assert.True(JobStateMachine.CanTransition(JobKind.WorldBackup, current, next));
        }

        [Theory]
        [InlineData(JobStatus.Queued, JobStatus.PendingRestart)]
        [InlineData(JobStatus.PendingRestart, JobStatus.Running)]
        [InlineData(JobStatus.PendingRestart, JobStatus.Cancelled)]
        [InlineData(JobStatus.Running, JobStatus.Succeeded)]
        [InlineData(JobStatus.Running, JobStatus.Failed)]
        [InlineData(JobStatus.Running, JobStatus.ResultUnknown)]
        public void Restore_has_an_explicit_pending_restart_phase(
            JobStatus current,
            JobStatus next)
        {
            Assert.True(JobStateMachine.CanTransition(JobKind.Restore, current, next));
        }

        [Fact]
        public void Ordinary_jobs_cannot_enter_pending_restart()
        {
            foreach (var kind in Enum.GetValues(typeof(JobKind)).Cast<JobKind>())
            {
                if (kind == JobKind.Restore)
                    continue;

                Assert.False(JobStateMachine.CanTransition(kind, JobStatus.Queued, JobStatus.PendingRestart));
                Assert.False(JobStateMachine.CanTransition(kind, JobStatus.Running, JobStatus.PendingRestart));
            }
        }

        [Fact]
        public void Terminal_states_cannot_transition_again()
        {
            var terminalStates = new[]
            {
                JobStatus.Succeeded,
                JobStatus.Failed,
                JobStatus.Cancelled,
                JobStatus.Interrupted,
                JobStatus.ResultUnknown
            };

            foreach (var terminal in terminalStates)
            foreach (var next in Enum.GetValues(typeof(JobStatus)).Cast<JobStatus>())
                Assert.False(JobStateMachine.CanTransition(JobKind.Restore, terminal, next));
        }

        [Fact]
        public void Undefined_enum_values_are_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                JobStateMachine.CanTransition((JobKind)99, JobStatus.Queued, JobStatus.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                JobStateMachine.CanTransition(JobKind.WorldBackup, (JobStatus)99, JobStatus.Running));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                JobStateMachine.CanTransition(JobKind.WorldBackup, JobStatus.Queued, (JobStatus)99));
        }
    }
}
