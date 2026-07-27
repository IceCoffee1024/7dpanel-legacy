using System;

namespace LSTY.SevenDPanel.Domain.Rewards
{
    public enum GrantOperationState
    {
        Reserved,
        Dispatching,
        PendingReconciliation,
        Completed,
        Failed,
        Refunded,
        Compensated
    }

    public enum GrantDispatchResult
    {
        Succeeded,
        Failed,
        Unknown
    }

    public static class GrantStateMachine
    {
        public static bool CanTransition(GrantOperationState current, GrantOperationState next)
        {
            RequireDefined(current, nameof(current));
            RequireDefined(next, nameof(next));

            return current switch
            {
                GrantOperationState.Reserved =>
                    next == GrantOperationState.Dispatching ||
                    next == GrantOperationState.Failed ||
                    next == GrantOperationState.Refunded,
                GrantOperationState.Dispatching =>
                    next == GrantOperationState.PendingReconciliation ||
                    next == GrantOperationState.Completed ||
                    next == GrantOperationState.Failed,
                GrantOperationState.PendingReconciliation =>
                    next == GrantOperationState.Completed ||
                    next == GrantOperationState.Failed ||
                    next == GrantOperationState.Refunded ||
                    next == GrantOperationState.Compensated,
                GrantOperationState.Completed =>
                    next == GrantOperationState.Refunded ||
                    next == GrantOperationState.Compensated,
                _ => false
            };
        }

        public static GrantOperationState ResolveDispatchResult(
            GrantOperationState current,
            GrantDispatchResult result)
        {
            RequireDefined(current, nameof(current));
            RequireDefined(result, nameof(result));
            if (current != GrantOperationState.Dispatching)
                throw new InvalidOperationException("Only a dispatching grant can resolve delivery.");

            return result switch
            {
                GrantDispatchResult.Succeeded => GrantOperationState.Completed,
                GrantDispatchResult.Failed => GrantOperationState.Failed,
                GrantDispatchResult.Unknown => GrantOperationState.PendingReconciliation,
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
