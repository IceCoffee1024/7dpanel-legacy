namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public interface IConsoleCommandAuditStore
    {
        void Append(ConsoleCommandAuditEntry entry);

        void AppendGap(ConsoleCommandAuditGap gap);
    }
}