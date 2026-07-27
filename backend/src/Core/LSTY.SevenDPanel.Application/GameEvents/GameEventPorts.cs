namespace LSTY.SevenDPanel.Application.GameEvents
{
    public interface IGameEventStore
    {
        void Append(GameEventRecord record);
        void AppendGap(GameEventGap gap);
        GameEventPage Query(GameEventQuery query);
    }
}
