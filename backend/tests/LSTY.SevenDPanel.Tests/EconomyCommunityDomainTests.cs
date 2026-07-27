using LSTY.SevenDPanel.Domain.Community;
using LSTY.SevenDPanel.Domain.Economy;
using LSTY.SevenDPanel.Domain.Rewards;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class EconomyCommunityDomainTests
    {
        [Fact]
        public void Ledger_requires_non_negative_balanced_amounts()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LedgerRules.ValidateAmount(-1));

            var balanced = new[]
            {
                new LedgerEntryAmount(LedgerSide.Debit, 75),
                new LedgerEntryAmount(LedgerSide.Credit, 50),
                new LedgerEntryAmount(LedgerSide.Credit, 25)
            };
            var unbalanced = new[]
            {
                new LedgerEntryAmount(LedgerSide.Debit, 75),
                new LedgerEntryAmount(LedgerSide.Credit, 74)
            };

            Assert.True(LedgerRules.IsBalanced(balanced));
            Assert.False(LedgerRules.IsBalanced(unbalanced));
            Assert.False(LedgerRules.IsBalanced(Array.Empty<LedgerEntryAmount>()));
        }

        [Fact]
        public void Player_accounts_cannot_overdraw_but_system_accounts_can_be_negative()
        {
            Assert.Equal(25, LedgerRules.Apply(100, LedgerSide.Debit, 75, isSystemAccount: false));
            Assert.Equal(125, LedgerRules.Apply(100, LedgerSide.Credit, 25, isSystemAccount: false));
            Assert.Throws<InvalidOperationException>(() =>
                LedgerRules.Apply(25, LedgerSide.Debit, 26, isSystemAccount: false));
            Assert.Equal(-1, LedgerRules.Apply(25, LedgerSide.Debit, 26, isSystemAccount: true));
        }

        [Fact]
        public void Unknown_grant_delivery_enters_reconciliation_and_never_auto_completes_or_fails()
        {
            Assert.Equal(
                GrantOperationState.PendingReconciliation,
                GrantStateMachine.ResolveDispatchResult(
                    GrantOperationState.Dispatching,
                    GrantDispatchResult.Unknown));
            Assert.NotEqual(
                GrantOperationState.Completed,
                GrantStateMachine.ResolveDispatchResult(
                    GrantOperationState.Dispatching,
                    GrantDispatchResult.Unknown));
            Assert.NotEqual(
                GrantOperationState.Failed,
                GrantStateMachine.ResolveDispatchResult(
                    GrantOperationState.Dispatching,
                    GrantDispatchResult.Unknown));
        }

        [Fact]
        public void Unknown_teleport_delivery_enters_reconciliation_and_never_auto_completes_or_fails()
        {
            Assert.Equal(
                TeleportOperationState.PendingReconciliation,
                TeleportStateMachine.ResolveDispatchResult(
                    TeleportOperationState.Dispatching,
                    TeleportDispatchResult.Unknown));
            Assert.NotEqual(
                TeleportOperationState.Completed,
                TeleportStateMachine.ResolveDispatchResult(
                    TeleportOperationState.Dispatching,
                    TeleportDispatchResult.Unknown));
            Assert.NotEqual(
                TeleportOperationState.Failed,
                TeleportStateMachine.ResolveDispatchResult(
                    TeleportOperationState.Dispatching,
                    TeleportDispatchResult.Unknown));
        }

        [Fact]
        public void Vote_rounds_settle_once_and_unknown_action_results_are_terminal()
        {
            Assert.True(VoteStateMachine.CanTransition(VoteRoundState.Open, VoteRoundState.Passed));
            Assert.False(VoteStateMachine.CanTransition(VoteRoundState.Passed, VoteRoundState.Rejected));
            Assert.True(VoteStateMachine.CanTransition(VoteRoundState.Passed, VoteRoundState.ActionQueued));
            Assert.True(VoteStateMachine.CanTransition(
                VoteRoundState.ActionQueued,
                VoteRoundState.ActionResultUnknown));
            Assert.False(VoteStateMachine.CanTransition(
                VoteRoundState.ActionResultUnknown,
                VoteRoundState.ActionQueued));
            Assert.False(VoteStateMachine.CanTransition(
                VoteRoundState.ActionResultUnknown,
                VoteRoundState.ActionSucceeded));
        }

        [Fact]
        public void State_machines_reject_undefined_values()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GrantStateMachine.CanTransition((GrantOperationState)99, GrantOperationState.Completed));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TeleportStateMachine.CanTransition(TeleportOperationState.Reserved, (TeleportOperationState)99));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                VoteStateMachine.CanTransition((VoteRoundState)99, VoteRoundState.Cancelled));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LedgerRules.Apply(0, (LedgerSide)99, 0, isSystemAccount: false));
        }
    }
}
