using System;

namespace LSTY.SevenDPanel.Domain.Community
{
    public enum TeleportOperationState
    {
        Reserved,
        Dispatching,
        PendingReconciliation,
        Completed,
        Failed,
        Refunded
    }

    public enum TeleportDispatchResult
    {
        Succeeded,
        Failed,
        Unknown
    }

    public static class TeleportStateMachine
    {
        public static bool CanTransition(TeleportOperationState current, TeleportOperationState next)
        {
            RequireDefined(current, nameof(current));
            RequireDefined(next, nameof(next));

            return current switch
            {
                TeleportOperationState.Reserved =>
                    next == TeleportOperationState.Dispatching ||
                    next == TeleportOperationState.Failed ||
                    next == TeleportOperationState.Refunded,
                TeleportOperationState.Dispatching =>
                    next == TeleportOperationState.PendingReconciliation ||
                    next == TeleportOperationState.Completed ||
                    next == TeleportOperationState.Failed,
                TeleportOperationState.PendingReconciliation =>
                    next == TeleportOperationState.Completed ||
                    next == TeleportOperationState.Failed ||
                    next == TeleportOperationState.Refunded,
                TeleportOperationState.Completed => next == TeleportOperationState.Refunded,
                _ => false
            };
        }

        public static TeleportOperationState ResolveDispatchResult(
            TeleportOperationState current,
            TeleportDispatchResult result)
        {
            RequireDefined(current, nameof(current));
            RequireDefined(result, nameof(result));
            if (current != TeleportOperationState.Dispatching)
                throw new InvalidOperationException("Only a dispatching teleport can resolve delivery.");

            return result switch
            {
                TeleportDispatchResult.Succeeded => TeleportOperationState.Completed,
                TeleportDispatchResult.Failed => TeleportOperationState.Failed,
                TeleportDispatchResult.Unknown => TeleportOperationState.PendingReconciliation,
                _ => throw new ArgumentOutOfRangeException(nameof(result))
            };
        }

        private static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
