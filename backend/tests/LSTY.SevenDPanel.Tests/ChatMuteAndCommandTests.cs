using System;
using System.Collections.Generic;
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
    }
}
