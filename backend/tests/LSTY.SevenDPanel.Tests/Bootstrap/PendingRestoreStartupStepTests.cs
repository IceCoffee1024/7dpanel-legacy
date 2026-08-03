using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.DependencyInjection;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class PendingRestoreStartupStepTests
    {
        [Fact]
        public void Successful_startup_exposes_each_restore_phase_in_execution_order()
        {
            var observed = new List<PendingRestoreStartupStage>();
            PendingRestoreStartupStep? step = null;
            step = new PendingRestoreStartupStep(
                () => observed.Add(step!.CurrentStage),
                () => observed.Add(step!.CurrentStage),
                () => observed.Add(step!.CurrentStage));

            Assert.Equal(PendingRestoreStartupStage.NotStarted, step.CurrentStage);
            Assert.Null(step.FailedStage);

            step.Execute();

            Assert.Equal(
                new[]
                {
                    PendingRestoreStartupStage.ApplyingPendingRestore,
                    PendingRestoreStartupStage.MigratingDatabase,
                    PendingRestoreStartupStage.ReconcilingRestoreResult
                },
                observed);
            Assert.Equal(PendingRestoreStartupStage.Completed, step.CurrentStage);
            Assert.Null(step.FailedStage);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Startup_failure_records_the_exact_phase_and_stops_following_work(int stageValue)
        {
            var failingStage = (PendingRestoreStartupStage)stageValue;
            var observed = new List<PendingRestoreStartupStage>();
            PendingRestoreStartupStep? step = null;
            Action action(PendingRestoreStartupStage stage) => () =>
            {
                observed.Add(step!.CurrentStage);
                if (stage == failingStage)
                    throw new InvalidOperationException("startup phase failed");
            };
            step = new PendingRestoreStartupStep(
                action(PendingRestoreStartupStage.ApplyingPendingRestore),
                action(PendingRestoreStartupStage.MigratingDatabase),
                action(PendingRestoreStartupStage.ReconcilingRestoreResult));

            Assert.Throws<InvalidOperationException>(() => step.Execute());

            Assert.Equal(PendingRestoreStartupStage.Failed, step.CurrentStage);
            Assert.Equal(failingStage, step.FailedStage);
            Assert.Equal(failingStage, observed[observed.Count - 1]);
            Assert.Equal((int)failingStage, observed.Count);
        }
    }
}

