using System;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public sealed class ConsoleCommandRequest
    {
        public ConsoleCommandRequest(string actorSubject, string command)
        {
            if (string.IsNullOrWhiteSpace(actorSubject))
                throw new ArgumentException("A console command actor is required.", nameof(actorSubject));
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("A console command is required.", nameof(command));

            ActorSubject = actorSubject;
            Command = command;
        }

        public string ActorSubject { get; }
        public string Command { get; }
    }
}