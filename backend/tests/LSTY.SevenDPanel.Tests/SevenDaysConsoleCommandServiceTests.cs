using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysConsoleCommandServiceTests
    {
        [Fact]
        public async Task Commands_are_dispatched_in_fifo_order_with_independent_results()
        {
            var dispatcher = new ControlledDispatcher();
            using var service = CreateService(dispatcher, queueCapacity: 2);
            service.Start();

            var first = service.ExecuteAsync(Request("first"), CancellationToken.None);
            await dispatcher.WaitForCallCountAsync(1);
            var second = service.ExecuteAsync(Request("second"), CancellationToken.None);
            var third = service.ExecuteAsync(Request("third"), CancellationToken.None);

            dispatcher.CompleteNext("first-output");
            await dispatcher.WaitForCallCountAsync(2);
            dispatcher.CompleteNext("second-output");
            await dispatcher.WaitForCallCountAsync(3);
            dispatcher.CompleteNext("third-output");

            Assert.Equal(new[] { "first", "second", "third" }, dispatcher.Commands);
            Assert.Equal(new[] { "first-output" }, (await first).Output);
            Assert.Equal(new[] { "second-output" }, (await second).Output);
            Assert.Equal(new[] { "third-output" }, (await third).Output);
        }

        [Fact]
        public async Task Waiting_capacity_is_bounded_without_counting_the_running_command()
        {
            var dispatcher = new ControlledDispatcher();
            using var service = CreateService(dispatcher, queueCapacity: 1);
            service.Start();

            var running = service.ExecuteAsync(Request("running"), CancellationToken.None);
            await dispatcher.WaitForCallCountAsync(1);
            var waiting = service.ExecuteAsync(Request("waiting"), CancellationToken.None);

            await Assert.ThrowsAsync<ConsoleCommandQueueFullException>(() =>
                service.ExecuteAsync(Request("overflow"), CancellationToken.None));

            dispatcher.CompleteNext("running-output");
            await dispatcher.WaitForCallCountAsync(2);
            dispatcher.CompleteNext("waiting-output");
            await Task.WhenAll(running, waiting);
            Assert.Equal(new[] { "running", "waiting" }, dispatcher.Commands);
        }

        [Fact]
        public async Task Cancellation_while_waiting_prevents_dispatch()
        {
            var dispatcher = new ControlledDispatcher();
            using var service = CreateService(dispatcher, queueCapacity: 1);
            service.Start();

            var running = service.ExecuteAsync(Request("running"), CancellationToken.None);
            await dispatcher.WaitForCallCountAsync(1);
            using var cancellation = new CancellationTokenSource();
            var waiting = service.ExecuteAsync(Request("cancelled"), cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            dispatcher.CompleteNext("running-output");
            await running;
            Assert.Equal(new[] { "running" }, dispatcher.Commands);
        }

        [Fact]
        public async Task Cancellation_after_dispatch_waits_for_the_real_result()
        {
            var dispatcher = new ControlledDispatcher();
            using var service = CreateService(dispatcher, queueCapacity: 1);
            service.Start();
            using var cancellation = new CancellationTokenSource();

            var execution = service.ExecuteAsync(Request("running"), cancellation.Token);
            await dispatcher.WaitForCallCountAsync(1);
            cancellation.Cancel();
            Assert.False(execution.IsCompleted);

            dispatcher.CompleteNext("real-output");

            Assert.Equal(new[] { "real-output" }, (await execution).Output);
        }

        [Fact]
        public async Task Dispatch_failure_does_not_stop_later_commands()
        {
            var dispatcher = new ControlledDispatcher();
            using var service = CreateService(dispatcher, queueCapacity: 1);
            service.Start();

            var failed = service.ExecuteAsync(Request("failed"), CancellationToken.None);
            await dispatcher.WaitForCallCountAsync(1);
            var later = service.ExecuteAsync(Request("later"), CancellationToken.None);
            dispatcher.FailNext(new InvalidOperationException("dispatch failed"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
            await dispatcher.WaitForCallCountAsync(2);
            dispatcher.CompleteNext("later-output");
            Assert.Equal(new[] { "later-output" }, (await later).Output);
        }

        [Fact]
        public async Task Stop_waits_for_running_command_and_rejects_unstarted_commands()
        {
            var dispatcher = new ControlledDispatcher();
            using var service = CreateService(dispatcher, queueCapacity: 1);
            service.Start();

            var running = service.ExecuteAsync(Request("running"), CancellationToken.None);
            await dispatcher.WaitForCallCountAsync(1);
            var waiting = service.ExecuteAsync(Request("waiting"), CancellationToken.None);
            var stop = Task.Run(service.Stop, TestContext.Current.CancellationToken);

            await WaitForStopToRejectNewCommandsAsync(service);
            Assert.False(stop.IsCompleted);
            dispatcher.CompleteNext("running-output");

            await stop;
            Assert.Equal(new[] { "running-output" }, (await running).Output);
            await Assert.ThrowsAsync<ConsoleCommandUnavailableException>(() => waiting);
            Assert.Equal(new[] { "running" }, dispatcher.Commands);
            service.Stop();
            service.MarkGameReady();
        }

        private static async Task WaitForStopToRejectNewCommandsAsync(
            SevenDaysConsoleCommandService service)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                try
                {
                    await service.ExecuteAsync(Request("late"), CancellationToken.None);
                }
                catch (ConsoleCommandUnavailableException)
                {
                    return;
                }
                catch (ConsoleCommandQueueFullException)
                {
                    await Task.Delay(10, timeout.Token);
                }
            }
        }

        private static SevenDaysConsoleCommandService CreateService(
            ControlledDispatcher dispatcher,
            int queueCapacity)
        {
            return new SevenDaysConsoleCommandService(
                queueCapacity,
                TimeSpan.FromSeconds(5),
                dispatcher.DispatchAsync,
                TimeSpan.FromSeconds(5));
        }

        private static ConsoleCommandRequest Request(string command) =>
            new ConsoleCommandRequest("owner", command);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "SevenDays")]

        private sealed class ControlledDispatcher
        {
            private readonly object sync = new();
            private readonly List<string> commands = new();
            private readonly Queue<PendingCall> pending = new();

            public IReadOnlyList<string> Commands
            {
                get { lock (sync) return commands.ToArray(); }
            }

            public Task<ConsoleCommandResult> DispatchAsync(
                ConsoleCommandRequest request,
                TimeSpan startTimeout,
                CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<ConsoleCommandResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                lock (sync)
                {
                    commands.Add(request.Command);
                    pending.Enqueue(new PendingCall(request.Command, completion));
                }
                return completion.Task;
            }

            public void CompleteNext(string output)
            {
                PendingCall call;
                lock (sync)
                {
                    call = pending.Dequeue();
                }
                call.Completion.SetResult(new ConsoleCommandResult(
                    call.Command,
                    new[] { output }));
            }

            public void FailNext(Exception exception)
            {
                PendingCall call;
                lock (sync)
                {
                    call = pending.Dequeue();
                }
                call.Completion.SetException(exception);
            }

            public async Task WaitForCallCountAsync(int expected)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (true)
                {
                    lock (sync)
                    {
                        if (commands.Count >= expected) return;
                    }
                    await Task.Delay(10, timeout.Token);
                }
            }

            [Trait("Capability", "Operations")]

            [Trait("Boundary", "SevenDays")]

            private sealed class PendingCall
            {
                public PendingCall(
                    string command,
                    TaskCompletionSource<ConsoleCommandResult> completion)
                {
                    Command = command;
                    Completion = completion;
                }

                public string Command { get; }
                public TaskCompletionSource<ConsoleCommandResult> Completion { get; }
            }
        }
    }
}