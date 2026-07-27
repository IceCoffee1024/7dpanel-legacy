using System;

namespace LSTY.SevenDPanel.Domain.Community
{
    public enum VoteRoundState
    {
        Open,
        Passed,
        Rejected,
        Expired,
        Cancelled,
        ActionQueued,
        ActionSucceeded,
        ActionFailed,
        ActionResultUnknown
    }

    public static class VoteStateMachine
    {
        public static bool CanTransition(VoteRoundState current, VoteRoundState next)
        {
            RequireDefined(current, nameof(current));
            RequireDefined(next, nameof(next));

            if (current == VoteRoundState.Open)
            {
                return next == VoteRoundState.Passed ||
                    next == VoteRoundState.Rejected ||
                    next == VoteRoundState.Expired ||
                    next == VoteRoundState.Cancelled;
            }

            if (current == VoteRoundState.Passed)
                return next == VoteRoundState.ActionQueued;

            return current == VoteRoundState.ActionQueued &&
                (next == VoteRoundState.ActionSucceeded ||
                 next == VoteRoundState.ActionFailed ||
                 next == VoteRoundState.ActionResultUnknown);
        }

        private static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
