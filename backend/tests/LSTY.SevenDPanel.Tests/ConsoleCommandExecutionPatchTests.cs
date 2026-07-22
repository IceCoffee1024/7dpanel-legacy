using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ConsoleCommandExecutionPatchTests
    {
        [Fact]
        public void Prefix_and_Postfix_preserve_http_source_tokens_and_output_snapshots()
        {
            var sharedTokens = new List<string> { "say", "Hello  world" };
            ConsoleCommandExecutionObservation? observation = null;
            using var subscription = ConsoleCommandExecutionPatch.Subscribe(value => observation = value);
            using var source = ConsoleCommandSourceContext.Push("7dpanel-http", "owner");

            ConsoleCommandExecutionPatch.CapturePrefix(
                "say \"Hello  world\"",
                "local-game",
                _ => sharedTokens,
                out var state);
            sharedTokens.Clear();
            var sharedOutput = new List<string> { "before mutation" };

            ConsoleCommandExecutionPatch.Postfix(sharedOutput, state);
            sharedOutput[0] = "after mutation";

            Assert.NotNull(observation);
            Assert.Equal("say \"Hello  world\"", observation.RawCommand);
            Assert.Equal(new[] { "say", "Hello  world" }, observation.Tokens);
            Assert.Equal(new[] { "before mutation" }, observation.Output);
            Assert.Equal("7dpanel-http", observation.Source);
            Assert.Equal("owner", observation.ActorSubject);
            Assert.Equal(ConsoleCommandCompletionKind.Completed, observation.CompletionKind);
        }

        [Theory]
        [InlineData(true, false, false, "local-game")]
        [InlineData(false, true, false, "remote-client")]
        [InlineData(false, false, true, "network")]
        public void Prefix_maps_native_sources_without_actor_subject(
            bool isLocalGame,
            bool hasRemoteClient,
            bool hasNetworkConnection,
            string expectedSource)
        {
            ConsoleCommandExecutionObservation? observation = null;
            using var subscription = ConsoleCommandExecutionPatch.Subscribe(value => observation = value);

            ConsoleCommandExecutionPatch.CapturePrefix(
                "help",
                ConsoleCommandExecutionPatch.ClassifySource(
                    isLocalGame,
                    hasRemoteClient,
                    hasNetworkConnection),
                _ => new List<string> { "help" },
                out var state);
            ConsoleCommandExecutionPatch.Postfix(new List<string>(), state);

            Assert.NotNull(observation);
            Assert.Equal(expectedSource, observation.Source);
            Assert.Null(observation.ActorSubject);
        }

        [Fact]
        public void Finalizer_publishes_once_and_returns_the_original_exception()
        {
            var observations = new List<ConsoleCommandExecutionObservation>();
            using var subscription = ConsoleCommandExecutionPatch.Subscribe(observations.Add);
            ConsoleCommandExecutionPatch.CapturePrefix(
                "broken",
                "local-game",
                _ => new List<string> { "broken" },
                out var state);
            var originalException = new InvalidOperationException("failure");

            var returnedException = ConsoleCommandExecutionPatch.Finalizer(originalException, state);
            ConsoleCommandExecutionPatch.Postfix(new List<string> { "late" }, state);

            Assert.Same(originalException, returnedException);
            var observation = Assert.Single(observations);
            Assert.Equal(ConsoleCommandCompletionKind.Threw, observation.CompletionKind);
            Assert.Equal(typeof(InvalidOperationException).FullName, observation.ExceptionType);
            Assert.Empty(observation.Output);
        }

        [Fact]
        public void Observer_failure_never_changes_the_original_result_or_exception()
        {
            using var subscription = ConsoleCommandExecutionPatch.Subscribe(
                _ => throw new InvalidOperationException("observer failure"));
            ConsoleCommandExecutionPatch.CapturePrefix(
                string.Empty,
                "local-game",
                _ => null,
                out var state);
            var output = new List<string> { "unchanged" };

            ConsoleCommandExecutionPatch.Postfix(output, state);
            var exception = new InvalidOperationException("original");

            Assert.Same(exception, ConsoleCommandExecutionPatch.Finalizer(exception, state));
            Assert.Equal(new[] { "unchanged" }, output);
        }

        [Fact]
        public void Tokenizer_failure_falls_back_to_empty_tokens_without_blocking_observation()
        {
            ConsoleCommandExecutionObservation? observation = null;
            using var subscription = ConsoleCommandExecutionPatch.Subscribe(value => observation = value);

            ConsoleCommandPatchState? state = null;
            var exception = Record.Exception(() => ConsoleCommandExecutionPatch.CapturePrefix(
                "thirdparty.sample",
                "local-game",
                _ => throw new InvalidOperationException("tokenizer failed"),
                out state));

            Assert.Null(exception);
            Assert.NotNull(state);
            ConsoleCommandExecutionPatch.Postfix(new List<string> { "output" }, state);
            Assert.NotNull(observation);
            Assert.Empty(observation.Tokens);
            Assert.Equal(new[] { "output" }, observation.Output);
        }
    }
}