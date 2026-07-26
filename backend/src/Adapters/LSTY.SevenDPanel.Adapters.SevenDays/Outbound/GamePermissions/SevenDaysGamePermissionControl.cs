using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GamePermissions
{
    public sealed class SevenDaysGamePermissionControl : IGamePermissionControl
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private readonly Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher;
        private readonly INativeGamePermissionControl native;

        public SevenDaysGamePermissionControl()
            : this(
                (name, action, timeout, cancellationToken) => GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                new NativeGamePermissionControl())
        {
        }

        internal SevenDaysGamePermissionControl(
            Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher,
            INativeGamePermissionControl native)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.native = native ?? throw new ArgumentNullException(nameof(native));
        }

        public async Task<IReadOnlyList<GameAdminEntry>> GetAdminsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var entries = await DispatchAsync("7DPanel.GamePermissions.GetAdmins", native.GetAdmins, cancellationToken).ConfigureAwait(false);
                return Array.AsReadOnly(entries.ToArray());
            }
            catch (TimeoutException) { throw new GamePermissionGameNotReadyException(); }
        }

        public async Task<IReadOnlyList<CommandPermissionEntry>> GetCommandsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var entries = await DispatchAsync("7DPanel.GamePermissions.GetCommands", native.GetCommands, cancellationToken).ConfigureAwait(false);
                return Array.AsReadOnly(entries.ToArray());
            }
            catch (TimeoutException) { throw new GamePermissionGameNotReadyException(); }
        }

        public Task<GamePermissionMutationResult> UpsertAdminAsync(GameAdminEntry entry, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.GamePermissions.UpsertAdmin", () => native.UpsertAdmin(entry), cancellationToken);
        public Task<GamePermissionMutationResult> RemoveAdminAsync(string playerId, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.GamePermissions.RemoveAdmin", () => native.RemoveAdmin(playerId), cancellationToken);
        public Task<GamePermissionMutationResult> UpsertCommandAsync(string command, int level, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.GamePermissions.UpsertCommand", () => native.UpsertCommand(command, level), cancellationToken);
        public Task<GamePermissionMutationResult> RemoveCommandAsync(string command, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.GamePermissions.RemoveCommand", () => native.RemoveCommand(command), cancellationToken);

        private async Task<T> DispatchAsync<T>(string name, Func<T> action, CancellationToken cancellationToken)
        {
            var result = await dispatcher(name, () => action()!, DispatchTimeout, cancellationToken).ConfigureAwait(false);
            return (T)result;
        }

        private async Task<GamePermissionMutationResult> MutateAsync(
            string name,
            Func<GamePermissionMutationResult> action,
            CancellationToken cancellationToken)
        {
            try { return await DispatchAsync(name, action, cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { return GamePermissionMutationResult.GameNotReady(); }
            catch (GamePermissionGameNotReadyException) { return GamePermissionMutationResult.GameNotReady(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { return GamePermissionMutationResult.NativeRejected(exception.GetType().Name); }
        }
    }

    internal interface INativeGamePermissionControl
    {
        IReadOnlyList<GameAdminEntry> GetAdmins();
        IReadOnlyList<CommandPermissionEntry> GetCommands();
        GamePermissionMutationResult UpsertAdmin(GameAdminEntry entry);
        GamePermissionMutationResult RemoveAdmin(string playerId);
        GamePermissionMutationResult UpsertCommand(string command, int level);
        GamePermissionMutationResult RemoveCommand(string command);
    }

    internal sealed class NativeGamePermissionControl : INativeGamePermissionControl
    {
        public IReadOnlyList<GameAdminEntry> GetAdmins()
        {
            var users = global::GameManager.Instance?.adminTools?.Users;
            if (users == null) throw new GamePermissionGameNotReadyException();
            return users.GetUsers().Values.Select(value => new GameAdminEntry(
                value.UserIdentifier?.CombinedString ?? string.Empty,
                value.Name ?? string.Empty,
                value.PermissionLevel)).ToArray();
        }

        public IReadOnlyList<CommandPermissionEntry> GetCommands()
        {
            var commands = global::GameManager.Instance?.adminTools?.Commands;
            if (commands == null) throw new GamePermissionGameNotReadyException();
            return commands.GetCommands().Values.Select(value => new CommandPermissionEntry(
                value.Command,
                value.PermissionLevel,
                TryGetDescription(value.Command))).ToArray();
        }

        public GamePermissionMutationResult UpsertAdmin(GameAdminEntry entry)
        {
            var users = global::GameManager.Instance?.adminTools?.Users;
            if (users == null) return GamePermissionMutationResult.GameNotReady();
            if (!global::PlatformUserIdentifierAbs.TryFromCombinedString(entry.PlayerId, out var identifier))
                return GamePermissionMutationResult.Invalid("invalid_player_id");
            users.AddUser(entry.DisplayName, identifier!, entry.PermissionLevel);
            return GamePermissionMutationResult.Succeeded();
        }

        public GamePermissionMutationResult RemoveAdmin(string playerId)
        {
            var users = global::GameManager.Instance?.adminTools?.Users;
            if (users == null) return GamePermissionMutationResult.GameNotReady();
            if (!global::PlatformUserIdentifierAbs.TryFromCombinedString(playerId, out var identifier))
                return GamePermissionMutationResult.Invalid("invalid_player_id");
            return users.RemoveUser(identifier!, true)
                ? GamePermissionMutationResult.Succeeded()
                : GamePermissionMutationResult.NotFound();
        }

        public GamePermissionMutationResult UpsertCommand(string command, int level)
        {
            var commands = global::GameManager.Instance?.adminTools?.Commands;
            if (commands == null) return GamePermissionMutationResult.GameNotReady();
            commands.AddCommand(command, level, true);
            return GamePermissionMutationResult.Succeeded();
        }

        public GamePermissionMutationResult RemoveCommand(string command)
        {
            var commands = global::GameManager.Instance?.adminTools?.Commands;
            if (commands == null) return GamePermissionMutationResult.GameNotReady();
            return commands.RemoveCommand(new[] { command })
                ? GamePermissionMutationResult.Succeeded()
                : GamePermissionMutationResult.NotFound();
        }

        private static string? TryGetDescription(string command)
        {
            try
            {
                return global::SingletonMonoBehaviour<global::SdtdConsole>.Instance
                    ?.GetCommand(command)?.GetDescription();
            }
            catch { return null; }
        }
    }
}
