using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Commerce
{
    public interface ICommerceStore
    {
        ShopProductSnapshot SaveProduct(ShopProductDraft product, DateTimeOffset occurredAtUtc);
        ShopProductSnapshot GetProduct(string productId);
        PurchaseReservationResult ReservePurchase(PurchaseReservationRequest request);
        ShopPurchaseSnapshot GetPurchase(string purchaseId);
        ShopPurchaseSnapshot? TryStartPurchaseDispatch(string purchaseId, DateTimeOffset occurredAtUtc);
        ShopPurchaseSnapshot ResolvePurchaseGrant(PurchaseGrantResolution resolution);

        RedeemCodeSnapshot SaveRedeemCode(
            RedeemCodeSecretDraft definition,
            DateTimeOffset occurredAtUtc);
        RedeemCodeSnapshot GetRedeemCode(string codeId);
        RedemptionReservationResult ReserveRedemption(RedeemReservationRequest request);
        RedeemAttemptSnapshot ResolveRedemptionGrant(RedeemGrantResolution resolution);

        AchievementDefinitionSnapshot SaveAchievement(
            AchievementDefinitionDraft definition,
            DateTimeOffset occurredAtUtc);
        AchievementProgressSnapshot GetAchievementProgress(
            string achievementId,
            string crossplatformId);
        IReadOnlyList<RewardEligibilitySnapshot> ObserveAchievement(
            ObserveAchievementCommand observation);

        OnlineRewardRuleSnapshot SaveOnlineRewardRule(
            OnlineRewardRuleDraft rule,
            DateTimeOffset occurredAtUtc);
        IReadOnlyList<RewardEligibilitySnapshot> EvaluateOnlineRewards(
            EvaluateOnlineRewardsCommand command);
        RewardEligibilitySnapshot ReserveManualOnlineReward(ManualOnlineRewardCommand command);

        RewardEligibilitySnapshot? TryReserveEligibilityGrant(
            string eligibilityId,
            DateTimeOffset occurredAtUtc);
        RewardEligibilitySnapshot ResolveEligibilityGrant(
            EligibilityGrantResolution resolution);
        IReadOnlyList<RewardEligibilitySnapshot> ListEligibilities(
            string ruleKind,
            string ruleId,
            string crossplatformId);
    }
}
