namespace LSTY.SevenDPanel.Application
{
    public sealed class KickPlayerRequest
    {
        public KickPlayerRequest(
            string actorSubject,
            int entityId,
            PlayerPlatformIdentity expectedPlatformIdentity,
            string reason,
            bool confirmed)
        {
            ActorSubject = actorSubject;
            EntityId = entityId;
            ExpectedPlatformIdentity = expectedPlatformIdentity;
            Reason = reason;
            Confirmed = confirmed;
        }

        public string ActorSubject { get; }

        public int EntityId { get; }

        public PlayerPlatformIdentity ExpectedPlatformIdentity { get; }

        public string Reason { get; }

        public bool Confirmed { get; }
    }
}