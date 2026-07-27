using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public static class CommunityGameCommandConsumerSet
    {
        private const string DailyCommandRuleId = "daily";

        private static readonly IReadOnlyList<CommunityGameCommandId> FixedContractGaps =
            Array.Empty<CommunityGameCommandId>();

        public static IReadOnlyList<CommunityGameCommandId> ContractGapCommands => FixedContractGaps;

        public static IReadOnlyList<ICommunityGameCommandConsumer> Create(
            OpenPlayerAccountUseCase openAccount,
            TransferBalanceUseCase transferBalance,
            QueryEconomyAccountsUseCase queryAccounts,
            BrowseShopUseCase browseShop,
            PurchaseProductUseCase purchaseProduct,
            RedeemCodeUseCase redeemCode,
            ClaimDailyRewardUseCase dailyRewards,
            HomeUseCases homes,
            CityUseCases cities,
            TeleportUseCases teleports,
            TeleportFriendRequestUseCases teleportFriendRequests,
            ICommunityPlayerCommandSnapshotProvider players,
            ICommunityGameCommandConsumer voteKick,
            ICommunityGameCommandConsumer voteRestart,
            Func<CommunityGameCommandId, bool> isEnabled,
            Func<DateTimeOffset>? utcClock = null,
            Func<string>? idFactory = null)
        {
            if (openAccount == null) throw new ArgumentNullException(nameof(openAccount));
            if (transferBalance == null) throw new ArgumentNullException(nameof(transferBalance));
            if (queryAccounts == null) throw new ArgumentNullException(nameof(queryAccounts));
            if (browseShop == null) throw new ArgumentNullException(nameof(browseShop));
            if (purchaseProduct == null) throw new ArgumentNullException(nameof(purchaseProduct));
            if (redeemCode == null) throw new ArgumentNullException(nameof(redeemCode));
            if (dailyRewards == null) throw new ArgumentNullException(nameof(dailyRewards));
            if (homes == null) throw new ArgumentNullException(nameof(homes));
            if (cities == null) throw new ArgumentNullException(nameof(cities));
            if (teleports == null) throw new ArgumentNullException(nameof(teleports));
            if (teleportFriendRequests == null)
                throw new ArgumentNullException(nameof(teleportFriendRequests));
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (voteKick == null) throw new ArgumentNullException(nameof(voteKick));
            if (voteRestart == null) throw new ArgumentNullException(nameof(voteRestart));
            if (voteKick.Command != CommunityGameCommandId.VoteKick)
                throw new ArgumentException("The vote-kick consumer has the wrong command id.", nameof(voteKick));
            if (voteRestart.Command != CommunityGameCommandId.VoteRestart)
                throw new ArgumentException("The vote-restart consumer has the wrong command id.", nameof(voteRestart));
            if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));

            var execution = new CommandExecution(
                openAccount,
                transferBalance,
                queryAccounts,
                browseShop,
                purchaseProduct,
                redeemCode,
                dailyRewards,
                homes,
                cities,
                teleports,
                teleportFriendRequests,
                players,
                utcClock ?? (() => DateTimeOffset.UtcNow),
                idFactory ?? (() => Guid.NewGuid().ToString("N")));

            return new ICommunityGameCommandConsumer[]
            {
                Enabled(CommunityGameCommandId.Balance, execution.Balance, isEnabled),
                Enabled(CommunityGameCommandId.Pay, execution.Pay, isEnabled),
                Enabled(CommunityGameCommandId.MoneyTop, execution.MoneyTop, isEnabled),
                Enabled(CommunityGameCommandId.Daily, execution.Daily, isEnabled),
                Enabled(CommunityGameCommandId.Shop, execution.Shop, isEnabled),
                Enabled(CommunityGameCommandId.Buy, execution.Buy, isEnabled),
                Enabled(CommunityGameCommandId.Redeem, execution.Redeem, isEnabled),
                Enabled(CommunityGameCommandId.Homes, execution.ListHomes, isEnabled),
                Enabled(CommunityGameCommandId.SetHome, execution.SetHome, isEnabled),
                Enabled(CommunityGameCommandId.DeleteHome, execution.DeleteHome, isEnabled),
                Enabled(CommunityGameCommandId.Home, execution.TeleportHome, isEnabled),
                Enabled(CommunityGameCommandId.Cities, execution.ListCities, isEnabled),
                Enabled(CommunityGameCommandId.City, execution.TeleportCity, isEnabled),
                Enabled(CommunityGameCommandId.TeleportAsk, execution.RequestTeleportFriend, isEnabled),
                Enabled(CommunityGameCommandId.TeleportAccept, execution.AcceptTeleportFriend, isEnabled),
                Enabled(CommunityGameCommandId.TeleportReject, execution.RejectTeleportFriend, isEnabled),
                Enabled(CommunityGameCommandId.Back, execution.TeleportBack, isEnabled),
                voteKick,
                voteRestart
            };
        }

        private static ICommunityGameCommandConsumer Enabled(
            CommunityGameCommandId command,
            Func<CommunityGameCommandContext, CommunityCommandConsumerResult> execute,
            Func<CommunityGameCommandId, bool> isEnabled) =>
            new FixedConsumer(command, () => isEnabled(command), execute);

        private static ICommunityGameCommandConsumer Unavailable(CommunityGameCommandId command) =>
            new FixedConsumer(command, () => false, _ => CommunityCommandConsumerResult.Failed());

        private sealed class FixedConsumer : ICommunityGameCommandConsumer
        {
            private readonly Func<bool> isEnabled;
            private readonly Func<CommunityGameCommandContext, CommunityCommandConsumerResult> execute;

            public FixedConsumer(
                CommunityGameCommandId command,
                Func<bool> isEnabled,
                Func<CommunityGameCommandContext, CommunityCommandConsumerResult> execute)
            {
                Command = command;
                this.isEnabled = isEnabled;
                this.execute = execute;
            }

            public CommunityGameCommandId Command { get; }
            public bool IsEnabled => isEnabled();
            public CommunityCommandConsumerResult Execute(CommunityGameCommandContext context) =>
                execute(context ?? throw new ArgumentNullException(nameof(context)));
        }

        private sealed class CommandExecution
        {
            private readonly OpenPlayerAccountUseCase openAccount;
            private readonly TransferBalanceUseCase transferBalance;
            private readonly QueryEconomyAccountsUseCase queryAccounts;
            private readonly BrowseShopUseCase browseShop;
            private readonly PurchaseProductUseCase purchaseProduct;
            private readonly RedeemCodeUseCase redeemCode;
            private readonly ClaimDailyRewardUseCase dailyRewards;
            private readonly HomeUseCases homes;
            private readonly CityUseCases cities;
            private readonly TeleportUseCases teleports;
            private readonly TeleportFriendRequestUseCases teleportFriendRequests;
            private readonly ICommunityPlayerCommandSnapshotProvider players;
            private readonly Func<DateTimeOffset> utcClock;
            private readonly Func<string> idFactory;

            public CommandExecution(
                OpenPlayerAccountUseCase openAccount,
                TransferBalanceUseCase transferBalance,
                QueryEconomyAccountsUseCase queryAccounts,
                BrowseShopUseCase browseShop,
                PurchaseProductUseCase purchaseProduct,
                RedeemCodeUseCase redeemCode,
                ClaimDailyRewardUseCase dailyRewards,
                HomeUseCases homes,
                CityUseCases cities,
                TeleportUseCases teleports,
                TeleportFriendRequestUseCases teleportFriendRequests,
                ICommunityPlayerCommandSnapshotProvider players,
                Func<DateTimeOffset> utcClock,
                Func<string> idFactory)
            {
                this.openAccount = openAccount;
                this.transferBalance = transferBalance;
                this.queryAccounts = queryAccounts;
                this.browseShop = browseShop;
                this.purchaseProduct = purchaseProduct;
                this.redeemCode = redeemCode;
                this.dailyRewards = dailyRewards;
                this.homes = homes;
                this.cities = cities;
                this.teleports = teleports;
                this.teleportFriendRequests = teleportFriendRequests;
                this.players = players;
                this.utcClock = utcClock;
                this.idFactory = idFactory;
            }

            public CommunityCommandConsumerResult Balance(CommunityGameCommandContext context)
            {
                var account = openAccount.Execute(new OpenPlayerAccountCommand(
                    context.CrossplatformId,
                    Id("balance-account"),
                    0,
                    UtcNow()));
                return CommunityCommandConsumerResult.Succeeded(
                    "balance=" + account.AvailableBalance.ToString(CultureInfo.InvariantCulture));
            }

            public CommunityCommandConsumerResult Pay(CommunityGameCommandContext context)
            {
                var target = players.ResolveOnline(context.Arguments[0]);
                if (target == null) return CommunityCommandConsumerResult.Rejected("target_not_found");
                if (string.Equals(target.CrossplatformId, context.CrossplatformId, StringComparison.Ordinal))
                    return CommunityCommandConsumerResult.Rejected("target_not_allowed");
                var amount = long.Parse(context.Arguments[1], CultureInfo.InvariantCulture);
                var operationId = Id("pay");
                try
                {
                    transferBalance.Execute(new TransferBalanceCommand(
                        operationId,
                        "game-command:" + operationId,
                        context.CrossplatformId,
                        target.CrossplatformId,
                        amount,
                        UtcNow(),
                        operationId));
                    return CommunityCommandConsumerResult.Succeeded(
                        "amount=" + amount.ToString(CultureInfo.InvariantCulture),
                        "target=" + target.DisplayName);
                }
                catch (EconomyException exception)
                {
                    return CommunityCommandConsumerResult.Rejected(exception.Message);
                }
            }

            public CommunityCommandConsumerResult MoneyTop(CommunityGameCommandContext context)
            {
                var take = context.Arguments.Count == 0
                    ? 10
                    : int.Parse(context.Arguments[0], CultureInfo.InvariantCulture);
                var page = queryAccounts.Execute(new AccountKeysetQuery(
                    Math.Min(take, AccountKeysetQuery.MaximumPageSize),
                    includeSystem: false,
                    enabled: true));
                var messages = page.Accounts
                    .Select((account, index) =>
                        "rank=" + (index + 1).ToString(CultureInfo.InvariantCulture) +
                        ";player=" + (account.CrossplatformId ?? "unknown") +
                        ";balance=" + account.AvailableBalance.ToString(CultureInfo.InvariantCulture))
                    .ToArray();
                return CommunityCommandConsumerResult.Succeeded(messages);
            }

            public CommunityCommandConsumerResult Shop(CommunityGameCommandContext context)
            {
                var page = browseShop.Execute(
                    new ShopProductKeysetQuery(ShopProductKeysetQuery.MaximumPageSize));
                var messages = page.Products
                    .Select(product =>
                        "product=" + product.ProductId +
                        ";name=" + product.Name +
                        ";price=" + product.PriceAmount.ToString(CultureInfo.InvariantCulture))
                    .ToArray();
                return CommunityCommandConsumerResult.Succeeded(messages);
            }

            public CommunityCommandConsumerResult Buy(CommunityGameCommandContext context)
            {
                var player = Current(context);
                if (player == null) return CommunityCommandConsumerResult.Rejected("player_not_online");
                var quantity = context.Arguments.Count == 1
                    ? 1
                    : int.Parse(context.Arguments[1], CultureInfo.InvariantCulture);
                var operationId = Id("buy");
                var result = purchaseProduct.ExecuteAsync(
                        new PurchaseProductCommand(
                            context.Arguments[0],
                            context.CrossplatformId,
                            player.Player.EntityId,
                            player.Player.Position.WorldId,
                            quantity,
                            "game-command:" + operationId,
                            operationId),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return result.Status == PurchaseRequestStatus.Completed
                    ? CommunityCommandConsumerResult.Succeeded("purchase=" + result.Purchase!.PurchaseId)
                    : CommunityCommandConsumerResult.Rejected(SnakeCase(result.Status));
            }

            public CommunityCommandConsumerResult Redeem(CommunityGameCommandContext context)
            {
                var player = Current(context);
                if (player == null) return CommunityCommandConsumerResult.Rejected("player_not_online");
                var operationId = Id("redeem");
                var result = redeemCode.ExecuteAsync(
                        new RedeemCodeCommand(
                            context.Arguments[0],
                            context.CrossplatformId,
                            player.Player.EntityId,
                            player.Player.Position.WorldId,
                            operationId),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return result.Status == RedeemRequestStatus.Succeeded
                    ? CommunityCommandConsumerResult.Succeeded()
                    : CommunityCommandConsumerResult.Rejected(SnakeCase(result.Status));
            }

            public CommunityCommandConsumerResult Daily(CommunityGameCommandContext context)
            {
                var player = Current(context);
                if (player == null) return CommunityCommandConsumerResult.Rejected("player_not_online");
                try
                {
                    var result = dailyRewards.ExecuteAsync(
                            new DailyRewardClaimCommand(
                                DailyCommandRuleId,
                                context.CrossplatformId,
                                player.Player.EntityId,
                                player.Player.Position.WorldId,
                                Id("daily")),
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    switch (result.Status)
                    {
                        case DailyRewardClaimStatus.Claimed:
                            return CommunityCommandConsumerResult.Succeeded(
                                "claim=" + result.Claim.ClaimId);
                        case DailyRewardClaimStatus.AlreadyClaimed:
                            return CommunityCommandConsumerResult.Rejected("already_claimed");
                        case DailyRewardClaimStatus.PendingReconciliation:
                            return CommunityCommandConsumerResult.Rejected("pending_reconciliation");
                        default:
                            return CommunityCommandConsumerResult.Rejected(
                                result.Claim.ErrorCode ?? "failed");
                    }
                }
                catch (DailyRewardPolicyUnavailableException)
                {
                    return CommunityCommandConsumerResult.Rejected("policy_unavailable");
                }
            }

            public CommunityCommandConsumerResult ListHomes(CommunityGameCommandContext context)
            {
                var messages = homes.List(context.CrossplatformId)
                    .Select(home => home.Name)
                    .ToArray();
                return CommunityCommandConsumerResult.Succeeded(messages);
            }

            public CommunityCommandConsumerResult SetHome(CommunityGameCommandContext context)
            {
                var player = Current(context);
                if (player == null) return CommunityCommandConsumerResult.Rejected("player_not_online");
                try
                {
                    var home = homes.Save(
                        StableHomeId(context.CrossplatformId, context.Arguments[0]),
                        context.Arguments[0],
                        player.Player);
                    return CommunityCommandConsumerResult.Succeeded(home.Name);
                }
                catch (CommunityException exception)
                {
                    return CommunityCommandConsumerResult.Rejected(exception.Code);
                }
            }

            public CommunityCommandConsumerResult DeleteHome(CommunityGameCommandContext context) =>
                homes.Delete(context.CrossplatformId, context.Arguments[0])
                    ? CommunityCommandConsumerResult.Succeeded(context.Arguments[0])
                    : CommunityCommandConsumerResult.Rejected("not_found");

            public CommunityCommandConsumerResult TeleportHome(CommunityGameCommandContext context) =>
                ExecuteTeleport(context, (request, cancellationToken) =>
                    teleports.TeleportHomeAsync(
                        request,
                        context.Arguments[0],
                        cancellationToken));

            public CommunityCommandConsumerResult ListCities(CommunityGameCommandContext context)
            {
                var messages = cities.ListEnabled()
                    .OrderBy(city => city.SortOrder)
                    .ThenBy(city => city.Name, StringComparer.Ordinal)
                    .Select(city => city.Name)
                    .ToArray();
                return CommunityCommandConsumerResult.Succeeded(messages);
            }

            public CommunityCommandConsumerResult TeleportCity(CommunityGameCommandContext context) =>
                ExecuteTeleport(context, (request, cancellationToken) =>
                    teleports.TeleportCityAsync(
                        request,
                        context.Arguments[0],
                        cancellationToken));

            public CommunityCommandConsumerResult TeleportBack(CommunityGameCommandContext context) =>
                ExecuteTeleport(context, teleports.TeleportBackAsync);

            public CommunityCommandConsumerResult RequestTeleportFriend(
                CommunityGameCommandContext context)
            {
                var result = teleportFriendRequests.Request(
                    context.CrossplatformId,
                    context.Arguments[0]);
                return result.Status == TeleportFriendRequestCreateStatus.Created
                    ? CommunityCommandConsumerResult.Succeeded(
                        "request=" + result.Request!.RequestId)
                    : CommunityCommandConsumerResult.Rejected(SnakeCase(result.Status));
            }

            public CommunityCommandConsumerResult AcceptTeleportFriend(
                CommunityGameCommandContext context)
            {
                try
                {
                    var result = teleportFriendRequests.AcceptAsync(
                            context.CrossplatformId,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    return result.Status == TeleportFriendRequestResponseStatus.Accepted
                        ? ToTeleportResult(result.TeleportOperation!)
                        : CommunityCommandConsumerResult.Rejected(SnakeCase(result.Status));
                }
                catch (CommunityException exception)
                {
                    return CommunityCommandConsumerResult.Rejected(exception.Code);
                }
            }

            public CommunityCommandConsumerResult RejectTeleportFriend(
                CommunityGameCommandContext context)
            {
                var result = teleportFriendRequests.Reject(context.CrossplatformId);
                return result.Status == TeleportFriendRequestResponseStatus.Rejected
                    ? CommunityCommandConsumerResult.Succeeded()
                    : CommunityCommandConsumerResult.Rejected(SnakeCase(result.Status));
            }

            private CommunityCommandConsumerResult ExecuteTeleport(
                CommunityGameCommandContext context,
                Func<TeleportExecutionRequest, CancellationToken, Task<TeleportOperation>> execute)
            {
                var player = Current(context);
                if (player == null) return CommunityCommandConsumerResult.Rejected("player_not_online");
                var operationId = Id("teleport");
                try
                {
                    var operation = execute(
                            new TeleportExecutionRequest(
                                operationId,
                                "game-command:" + operationId,
                                player.Player,
                                "Player",
                                context.CrossplatformId,
                                operationId),
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    return ToTeleportResult(operation);
                }
                catch (CommunityException exception)
                {
                    return CommunityCommandConsumerResult.Rejected(exception.Code);
                }
            }

            private CommunityPlayerCommandSnapshot? Current(CommunityGameCommandContext context) =>
                players.FindOnlineByCrossplatformId(context.CrossplatformId);

            private static CommunityCommandConsumerResult ToTeleportResult(
                TeleportOperation operation)
            {
                if (operation == null) return CommunityCommandConsumerResult.Failed();
                switch (operation.State)
                {
                    case TeleportOperationState.Completed:
                        return CommunityCommandConsumerResult.Succeeded(
                            "operation=" + operation.OperationId);
                    case TeleportOperationState.PendingReconciliation:
                        return CommunityCommandConsumerResult.Rejected("pending_reconciliation");
                    case TeleportOperationState.Failed:
                        return CommunityCommandConsumerResult.Rejected(
                            operation.ErrorCode ?? "failed");
                    case TeleportOperationState.Refunded:
                        return CommunityCommandConsumerResult.Rejected("refunded");
                    default:
                        return CommunityCommandConsumerResult.Rejected("pending");
                }
            }

            private DateTimeOffset UtcNow()
            {
                var value = utcClock();
                if (value.Offset != TimeSpan.Zero)
                    throw new InvalidOperationException("community_game_command_clock_not_utc");
                return value;
            }

            private string Id(string kind)
            {
                var value = idFactory();
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException("community_game_command_id_unavailable");
                return "community-" + kind + "-" + value.Trim();
            }

            private static string StableHomeId(string crossplatformId, string homeName)
            {
                using var sha256 = SHA256.Create();
                var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(
                    crossplatformId + "\n" + homeName));
                var builder = new StringBuilder("home-");
                for (var index = 0; index < 16; index++)
                    builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }

            private static string SnakeCase<T>(T value) where T : struct, Enum
            {
                var text = value.ToString();
                var builder = new StringBuilder(text.Length + 4);
                for (var index = 0; index < text.Length; index++)
                {
                    var character = text[index];
                    if (char.IsUpper(character) && index > 0) builder.Append('_');
                    builder.Append(char.ToLowerInvariant(character));
                }
                return builder.ToString();
            }
        }
    }
}
