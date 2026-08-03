using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GamePermissions;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Application")]
    public sealed class GamePermissionTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(2001)]
        public async Task Use_case_rejects_native_levels_outside_zero_to_two_thousand(int level)
        {
            var port = new RecordingControl();
            var useCases = new GamePermissionUseCases(port, new NoOpActivityWriter());

            var result = await useCases.UpsertCommandAsync(
                "owner", new CommandPermissionRequest("tele", level), CancellationToken.None);

            Assert.Equal(GamePermissionMutationStatus.Invalid, result.Status);
            Assert.Equal(0, port.MutationCalls);
        }

        [Fact]
        public async Task Adapter_copies_admin_entries_inside_dispatcher_boundary()
        {
            var insideDispatcher = false;
            var source = new List<GameAdminEntry> { new GameAdminEntry("EOS_1", "Player", 0) };
            var adapter = new SevenDaysGamePermissionControl(
                (_, action, _, _) =>
                {
                    insideDispatcher = true;
                    return Task.FromResult(action());
                },
                new DelegateNativeControl(getAdmins: () =>
                {
                    Assert.True(insideDispatcher);
                    return source;
                }));

            var entries = await adapter.GetAdminsAsync(CancellationToken.None);
            source.Clear();

            Assert.Single(entries);
            Assert.Equal("EOS_1", entries[0].PlayerId);
        }

        [Fact]
        public async Task Adapter_maps_dispatch_timeout_to_game_not_ready_without_native_call()
        {
            var nativeCalls = 0;
            var adapter = new SevenDaysGamePermissionControl(
                (_, _, _, _) => Task.FromException<object>(new TimeoutException()),
                new DelegateNativeControl(upsertCommand: (_, _) =>
                {
                    nativeCalls++;
                    return GamePermissionMutationResult.Succeeded();
                }));

            var result = await adapter.UpsertCommandAsync("tele", 100, CancellationToken.None);

            Assert.Equal(GamePermissionMutationStatus.GameNotReady, result.Status);
            Assert.Equal(0, nativeCalls);
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingControl : IGamePermissionControl
        {
            public int MutationCalls { get; private set; }
            public Task<IReadOnlyList<GameAdminEntry>> GetAdminsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameAdminEntry>>(Array.Empty<GameAdminEntry>());
            public Task<IReadOnlyList<CommandPermissionEntry>> GetCommandsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CommandPermissionEntry>>(Array.Empty<CommandPermissionEntry>());
            public Task<GamePermissionMutationResult> UpsertAdminAsync(GameAdminEntry entry, CancellationToken cancellationToken) { MutationCalls++; return Task.FromResult(GamePermissionMutationResult.Succeeded()); }
            public Task<GamePermissionMutationResult> RemoveAdminAsync(string playerId, CancellationToken cancellationToken) { MutationCalls++; return Task.FromResult(GamePermissionMutationResult.Succeeded()); }
            public Task<GamePermissionMutationResult> UpsertCommandAsync(string command, int level, CancellationToken cancellationToken) { MutationCalls++; return Task.FromResult(GamePermissionMutationResult.Succeeded()); }
            public Task<GamePermissionMutationResult> RemoveCommandAsync(string command, CancellationToken cancellationToken) { MutationCalls++; return Task.FromResult(GamePermissionMutationResult.Succeeded()); }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class DelegateNativeControl : INativeGamePermissionControl
        {
            private readonly Func<IReadOnlyList<GameAdminEntry>> getAdmins;
            private readonly Func<string, int, GamePermissionMutationResult> upsertCommand;
            public DelegateNativeControl(
                Func<IReadOnlyList<GameAdminEntry>>? getAdmins = null,
                Func<string, int, GamePermissionMutationResult>? upsertCommand = null)
            {
                this.getAdmins = getAdmins ?? (() => Array.Empty<GameAdminEntry>());
                this.upsertCommand = upsertCommand ?? ((_, _) => GamePermissionMutationResult.Succeeded());
            }
            public IReadOnlyList<GameAdminEntry> GetAdmins() => getAdmins();
            public IReadOnlyList<CommandPermissionEntry> GetCommands() => Array.Empty<CommandPermissionEntry>();
            public GamePermissionMutationResult UpsertAdmin(GameAdminEntry entry) => GamePermissionMutationResult.Succeeded();
            public GamePermissionMutationResult RemoveAdmin(string playerId) => GamePermissionMutationResult.Succeeded();
            public GamePermissionMutationResult UpsertCommand(string command, int level) => upsertCommand(command, level);
            public GamePermissionMutationResult RemoveCommand(string command) => GamePermissionMutationResult.Succeeded();
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class NoOpActivityWriter : IRecentActivityWriter
        {
            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
