using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IPlayerActions
    {
        Task<KickPlayerActionResult> KickAsync(
            KickPlayerCommand command,
            CancellationToken cancellationToken);
    }

    public sealed class KickPlayerCommand
    {
        internal KickPlayerCommand(
            int entityId,
            PlayerPlatformIdentity expectedPlatformIdentity,
            string reason)
        {
            EntityId = entityId;
            ExpectedPlatformIdentity = expectedPlatformIdentity;
            Reason = reason;
        }

        public int EntityId { get; }

        public PlayerPlatformIdentity ExpectedPlatformIdentity { get; }

        public string Reason { get; }
    }

    public enum KickPlayerActionStatus
    {
        Succeeded,
        PlayerNotOnline,
        PlayerIdentityChanged
    }

    public sealed class KickPlayerActionResult
    {
        private KickPlayerActionResult(
            KickPlayerActionStatus status,
            PlayerActionTarget? target)
        {
            Status = status;
            Target = target;
        }

        public KickPlayerActionStatus Status { get; }

        public PlayerActionTarget? Target { get; }

        public static KickPlayerActionResult Succeeded(
            int entityId,
            string name,
            PlayerPlatformIdentity platformIdentity)
        {
            return new KickPlayerActionResult(
                KickPlayerActionStatus.Succeeded,
                new PlayerActionTarget(entityId, name, platformIdentity));
        }

        public static KickPlayerActionResult PlayerNotOnline()
        {
            return new KickPlayerActionResult(KickPlayerActionStatus.PlayerNotOnline, null);
        }

        public static KickPlayerActionResult PlayerIdentityChanged(
            int entityId,
            string name,
            PlayerPlatformIdentity platformIdentity)
        {
            return new KickPlayerActionResult(
                KickPlayerActionStatus.PlayerIdentityChanged,
                new PlayerActionTarget(entityId, name, platformIdentity));
        }
    }

    public sealed class PlayerActionTarget
    {
        internal PlayerActionTarget(
            int entityId,
            string name,
            PlayerPlatformIdentity platformIdentity)
        {
            EntityId = entityId;
            Name = name;
            PlatformIdentity = platformIdentity;
        }

        public int EntityId { get; }

        public string Name { get; }

        public PlayerPlatformIdentity PlatformIdentity { get; }
    }
}