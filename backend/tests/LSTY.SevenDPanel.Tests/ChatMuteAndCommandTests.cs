using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Application.Chat;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ChatMuteAndCommandTests
    {
        [Fact]
        public void Help_command_is_case_insensitive_and_uses_the_fixed_result_codes()
        {
            var catalog = new GameChatCommandCatalog(new IGameChatCommandHandler[]
            {
                new HelpGameChatCommandHandler(() => true)
            });

            Assert.Equal("chat.command.help.succeeded", catalog.Handle("HELP", Context()).Code);
            Assert.Equal("chat.command.invalid_arguments", catalog.Handle("help", Context("extra")).Code);
            Assert.False(catalog.Handle("unknown", Context()).IsHandled);
        }

        [Fact]
        public void Catalog_replace_atomically_publishes_handlers_and_command_manifest()
        {
            var catalog = new GameChatCommandCatalog(new[]
            {
                Handler("old", "legacy")
            });

            catalog.Replace(new[] { Handler("new", "CURRENT") });

            var command = Assert.Single(catalog.Commands);
            Assert.Equal("new", command.Name);
            Assert.False(catalog.Handle("old", Context()).IsHandled);
            Assert.Equal("new", Assert.Single(catalog.Handle("current", Context()).Messages));
        }

        [Fact]
        public void Catalog_replace_conflict_keeps_the_previous_snapshot()
        {
            var catalog = new GameChatCommandCatalog(new[] { Handler("old", "legacy") });

            Assert.Throws<ArgumentException>(() => catalog.Replace(new[]
            {
                Handler("new", "shared"),
                Handler("SHARED")
            }));

            Assert.Equal("old", Assert.Single(catalog.Commands).Name);
            Assert.Equal("old", Assert.Single(catalog.Handle("LEGACY", Context()).Messages));
            Assert.False(catalog.Handle("new", Context()).IsHandled);
        }

        [Fact]
        public void Catalog_rebuild_uses_the_current_handlers_and_publishes_the_complete_result()
        {
            var catalog = new GameChatCommandCatalog(new[] { Handler("first") });

            catalog.Rebuild(current => current.Concat(new[] { Handler("second", "SECONDARY") }));

            Assert.Equal(new[] { "first", "second" }, catalog.Commands.Select(command => command.Name));
            Assert.Equal("first", Assert.Single(catalog.Handle("FIRST", Context()).Messages));
            Assert.Equal("second", Assert.Single(catalog.Handle("secondary", Context()).Messages));
        }

        [Fact]
        public void Reply_sender_exposes_only_a_private_result_path()
        {
            var type = typeof(SevenDaysGameChatCommandReplySender);

            Assert.NotNull(type.GetMethod("Send"));
            Assert.DoesNotContain(type.GetMethods(), method => method.Name.IndexOf("Global", StringComparison.OrdinalIgnoreCase) >= 0);

            var log = new List<string>();
            var handled = SevenDaysChatMessageCoordinator.DeliverHandledCommand(
                GameChatCommandResult.HelpSucceeded(new[] { "help" }),
                _ => throw new InvalidOperationException("offline"),
                log.Add);

            Assert.True(handled);
            Assert.Contains(log, entry => entry.IndexOf("reply failed", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static GameChatCommandContext Context(params string[] arguments) =>
            new GameChatCommandContext("EOS_1", "Alice", arguments);

        private static IGameChatCommandHandler Handler(string name, params string[] aliases) =>
            new StubCommandHandler(name, aliases);

        private sealed class StubCommandHandler : IGameChatCommandHandler
        {
            public StubCommandHandler(string name, IReadOnlyList<string> aliases)
            {
                Descriptor = new GameChatCommandDescriptor(name, aliases);
            }

            public GameChatCommandDescriptor Descriptor { get; }

            public GameChatCommandResult Handle(GameChatCommandContext context) =>
                GameChatCommandResult.HelpSucceeded(new[] { Descriptor.Name });
        }
    }
}
