using System;

namespace LSTY.SevenDPanel.Domain.Community
{
    public enum TeleportFriendRequestState
    {
        Pending,
        Accepted,
        Rejected,
        Expired,
        Cancelled
    }

    public static class TeleportFriendRequestStateMachine
    {
        public static bool CanTransition(
            TeleportFriendRequestState current,
            TeleportFriendRequestState next)
        {
            RequireDefined(current, nameof(current));
            RequireDefined(next, nameof(next));

            return current == TeleportFriendRequestState.Pending &&
                (next == TeleportFriendRequestState.Accepted ||
                 next == TeleportFriendRequestState.Rejected ||
                 next == TeleportFriendRequestState.Expired ||
                 next == TeleportFriendRequestState.Cancelled);
        }

        private static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
