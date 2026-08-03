using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Economy;
using LSTY.SevenDPanel.Domain.Rewards;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Economy")]
    [Trait("Boundary", "Web")]
    public sealed class CommerceRewardHttpTests
    {
        private const string HttpNamespace =
            "LSTY.SevenDPanel.Adapters.Web.Inbound.Http.";

        [Fact]
        public void Controllers_expose_only_the_approved_owner_routes_and_typed_bodies()
        {
            AssertController(
                "CommerceController",
                "api/v1",
                ("GetAccounts", "economy/accounts"),
                ("GetTransactions", "economy/transactions"),
                ("GetLeaderboard", "economy/leaderboard"),
                ("FreezeAccount", "economy/accounts/{accountId}/freeze"),
                ("AdjustBalance", "economy/accounts/{crossplatformId}/adjust"),
                ("GetProduct", "shop/products/{productId}"),
                ("PutProduct", "shop/products/{productId}"),
                ("GetPurchase", "shop/purchases/{purchaseId}"),
                ("Purchase", "shop/products/{productId}/purchases"),
                ("GetRedeemCode", "redeem-codes/{codeId}"),
                ("CreateRedeemCode", "redeem-codes"),
                ("Redeem", "redemptions"));
            AssertController(
                "RewardsController",
                "api/v1",
                ("GetPackage", "reward-packages/{packageId}"),
                ("PutPackage", "reward-packages/{packageId}"),
                ("GetGrant", "grant-operations/{operationId}"),
                ("GetPendingGrants", "grant-operations"),
                ("Grant", "grant-operations"),
                ("Confirm", "grant-operations/{operationId}/confirm"),
                ("Refund", "grant-operations/{operationId}/refund"),
                ("Compensate", "grant-operations/{operationId}/compensate"));
            AssertController(
                "AchievementsController",
                "api/v1/achievements",
                ("PutDefinition", "definitions/{achievementId}"),
                ("GetRecord", "records/{achievementId}/{crossplatformId}"));
            AssertController(
                "OnlineRewardsController",
                "api/v1/online-rewards",
                ("PutRule", "rules/{ruleId}"),
                ("GetRecords", "records"),
                ("ManualGrant", "records/manual"));

            var bodyTypes = new[]
            {
                "FreezeAccountHttpRequest",
                "AdjustBalanceHttpRequest",
                "ShopProductUpsertHttpRequest",
                "PurchaseProductHttpRequest",
                "CreateRedeemCodeHttpRequest",
                "RedeemHttpRequest",
                "RewardPackageUpsertHttpRequest",
                "GrantRewardHttpRequest",
                "RefundRewardGrantHttpRequest",
                "CompensateRewardGrantHttpRequest",
                "AchievementDefinitionUpsertHttpRequest",
                "OnlineRewardRuleUpsertHttpRequest",
                "ManualOnlineRewardGrantHttpRequest"
            }.Select(HttpType).ToArray();

            Assert.Equal(bodyTypes.Length, bodyTypes.Distinct().Count());
            foreach (var type in bodyTypes)
            {
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    Assert.NotEqual(typeof(object), property.PropertyType);
                    Assert.DoesNotMatch(
                        "(?:^|[a-z0-9])(?:Payload|Command|Script|Path|Sql|SQL)(?:$|[A-Z])",
                        property.Name);
                }
            }
        }

        [Theory]
        [InlineData("Owner", HttpStatusCode.OK)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        public async Task Economy_queries_are_owner_only(
            string? role,
            HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role);

            using var response = await host.Client.GetAsync(
                "api/v1/economy/accounts?limit=1",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        [Fact]
        public async Task Economy_queries_use_opaque_cursors_and_stable_400_and_503_problems()
        {
            using var host = CreateHost("Owner");

            using var first = await host.Client.GetAsync(
                "api/v1/economy/accounts?limit=1",
                TestContext.Current.CancellationToken);
            var page = JObject.Parse(await first.Content.ReadAsStringAsync());
            var cursor = (string?)page["nextCursor"];
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.NotNull(cursor);
            Assert.DoesNotContain("account-1", cursor!, StringComparison.Ordinal);

            using var second = await host.Client.GetAsync(
                "api/v1/economy/accounts?limit=1&cursor=" + Uri.EscapeDataString(cursor!),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            using var invalid = await host.Client.GetAsync(
                "api/v1/economy/accounts?cursor=not-a-cursor",
                TestContext.Current.CancellationToken);
            var invalidProblem = JObject.Parse(await invalid.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_economy_cursor", (string?)invalidProblem["code"]);

            using var unavailableHost = CreateHost("Owner", BackendMode.Unavailable);
            using var unavailable = await unavailableHost.Client.GetAsync(
                "api/v1/economy/accounts",
                TestContext.Current.CancellationToken);
            var unavailableProblem = JObject.Parse(await unavailable.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("economy_unavailable", (string?)unavailableProblem["code"]);
        }

        [Theory]
        [InlineData(BackendMode.EconomyConcurrency, "freeze", "economy_concurrency_conflict")]
        [InlineData(BackendMode.InsufficientFunds, "adjust", "economy_insufficient_funds")]
        [InlineData(BackendMode.EconomyIdempotency, "adjust", "economy_idempotency_conflict")]
        public async Task Economy_mutations_map_conflict_balance_and_idempotency(
            BackendMode mode,
            string operation,
            string expectedCode)
        {
            using var host = CreateHost("Owner", mode);
            var request = operation == "freeze"
                ? new HttpRequestMessage(HttpMethod.Post, "api/v1/economy/accounts/account-1/freeze")
                {
                    Content = Json("{\"isFrozen\":true,\"expectedRowVersion\":0}")
                }
                : new HttpRequestMessage(HttpMethod.Post, "api/v1/economy/accounts/EOS_1/adjust")
                {
                    Content = Json("{\"playerSide\":0,\"amount\":5,\"clientRequestKey\":\"request-1\",\"reason\":\"support\"}")
                };

            using (request)
            using (var response = await host.Client.SendAsync(
                       request,
                       TestContext.Current.CancellationToken))
            {
                var problem = JObject.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal(expectedCode, (string?)problem["code"]);
            }
        }

        [Fact]
        public async Task Commerce_maps_missing_products_and_insufficient_purchases()
        {
            using var missingHost = CreateHost("Owner");
            using var missing = await missingHost.Client.GetAsync(
                "api/v1/shop/products/missing",
                TestContext.Current.CancellationToken);
            var missingProblem = JObject.Parse(await missing.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("shop_product_not_found", (string?)missingProblem["code"]);

            using var purchaseHost = CreateHost("Owner", BackendMode.PurchaseInsufficientFunds);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "api/v1/shop/products/product-1/purchases")
            {
                Content = Json("{\"crossplatformId\":\"EOS_1\",\"expectedEntityId\":1,\"expectedWorldId\":\"world-1\",\"quantity\":1,\"clientRequestKey\":\"purchase-1\"}")
            };
            using var response = await purchaseHost.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("economy_insufficient_funds", (string?)problem["code"]);
        }

        [Fact]
        public async Task Redeem_code_plaintext_is_returned_once_and_never_leaks_the_digest()
        {
            using var host = CreateHost("Owner");
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/redeem-codes")
            {
                Content = Json("{\"rewardPackageId\":\"package-1\",\"enabled\":true,\"maxRedemptions\":5,\"perPlayerLimit\":1}")
            };
            using var created = await host.Client.SendAsync(
                createRequest,
                TestContext.Current.CancellationToken);
            var createdPayload = JObject.Parse(await created.Content.ReadAsStringAsync());
            var plaintext = (string?)createdPayload["code"];

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.True(created.Headers.CacheControl?.NoStore);
            Assert.False(string.IsNullOrWhiteSpace(plaintext));
            Assert.DoesNotContain("digest", createdPayload.ToString(), StringComparison.OrdinalIgnoreCase);

            using var fetched = await host.Client.GetAsync(
                "api/v1/redeem-codes/code-1",
                TestContext.Current.CancellationToken);
            var fetchedText = await fetched.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            Assert.DoesNotContain(plaintext!, fetchedText, StringComparison.Ordinal);
            Assert.DoesNotContain("digest", fetchedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("normalizedCode", fetchedText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Rewards_reject_arbitrary_registered_actions_and_map_idempotency_conflicts()
        {
            using var host = CreateHost("Owner");
            using var unsafeRequest = new HttpRequestMessage(
                HttpMethod.Put,
                "api/v1/reward-packages/package-1")
            {
                Content = Json("{\"name\":\"Unsafe\",\"description\":\"\",\"enabled\":true,\"sortOrder\":0,\"entries\":[{\"entryId\":\"entry-1\",\"kind\":2,\"registeredAction\":\"say hello\"}]}")
            };
            using var unsafeResponse = await host.Client.SendAsync(
                unsafeRequest,
                TestContext.Current.CancellationToken);
            var unsafeProblem = JObject.Parse(await unsafeResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, unsafeResponse.StatusCode);
            Assert.Equal("invalid_reward_package", (string?)unsafeProblem["code"]);

            using var conflictHost = CreateHost("Owner", BackendMode.RewardIdempotency);
            using var grantRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/grant-operations")
            {
                Content = Json("{\"packageId\":\"package-1\",\"crossplatformId\":\"EOS_1\",\"expectedEntityId\":1,\"expectedWorldId\":\"world-1\",\"clientRequestKey\":\"grant-1\"}")
            };
            using var conflict = await conflictHost.Client.SendAsync(
                grantRequest,
                TestContext.Current.CancellationToken);
            var conflictProblem = JObject.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("reward_idempotency_conflict", (string?)conflictProblem["code"]);
        }

        [Fact]
        public async Task Achievement_and_online_reward_contracts_query_configure_and_bound_manual_grants()
        {
            using var host = CreateHost("Owner");
            using var record = await host.Client.GetAsync(
                "api/v1/achievements/records/achievement-1/EOS_1",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, record.StatusCode);

            using var invalidDefinitionRequest = new HttpRequestMessage(
                HttpMethod.Put,
                "api/v1/achievements/definitions/achievement-1")
            {
                Content = Json("{\"name\":\"First\",\"description\":\"\",\"statistic\":0,\"thresholdValue\":-1,\"rewardPackageId\":\"package-1\",\"enabled\":true,\"sortOrder\":0}")
            };
            using var invalidDefinition = await host.Client.SendAsync(
                invalidDefinitionRequest,
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidDefinition.StatusCode);

            using var onlineRecords = await host.Client.GetAsync(
                "api/v1/online-rewards/records?ruleId=online-1&crossplatformId=EOS_1",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, onlineRecords.StatusCode);

            using var conflictHost = CreateHost("Owner", BackendMode.CommerceIdempotency);
            using var manualRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "api/v1/online-rewards/records/manual")
            {
                Content = Json("{\"ruleId\":\"online-1\",\"crossplatformId\":\"EOS_1\",\"expectedEntityId\":1,\"expectedWorldId\":\"world-1\",\"clientRequestKey\":\"manual-1\"}")
            };
            using var conflict = await conflictHost.Client.SendAsync(
                manualRequest,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("commerce_idempotency_conflict", (string?)problem["code"]);
        }

        [Fact]
        public async Task Delivery_endpoints_return_game_not_ready_problem_before_dispatch()
        {
            using var host = CreateHost("Owner", readiness: GameReadinessState.Loading);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "api/v1/shop/products/product-1/purchases")
            {
                Content = Json("{\"crossplatformId\":\"EOS_1\",\"expectedEntityId\":1,\"expectedWorldId\":\"world-1\",\"quantity\":1,\"clientRequestKey\":\"purchase-1\"}")
            };

            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("game_not_ready", (string?)problem["code"]);
        }

        private static void AssertController(
            string name,
            string prefix,
            params (string Method, string Route)[] routes)
        {
            var type = HttpType(name);
            Assert.Equal(prefix, type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix);
            Assert.Equal("Owner", type.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            foreach (var (methodName, routeTemplate) in routes)
            {
                var method = type.GetMethod(methodName);
                Assert.NotNull(method);
                Assert.Equal(routeTemplate, method!.GetCustomAttribute<RouteAttribute>()?.Template);
            }
        }

        private static Type HttpType(string name)
        {
            var type = typeof(AuditController).Assembly.GetType(HttpNamespace + name);
            Assert.NotNull(type);
            return type!;
        }

        private static StringContent Json(string value) =>
            new StringContent(value, System.Text.Encoding.UTF8, "application/json");

        private static HttpTestHost CreateHost(
            string? role,
            BackendMode mode = BackendMode.Normal,
            GameReadinessState readiness = GameReadinessState.Ready)
        {
            var backend = new StubBackend(mode);
            var services = new ServiceCollection();
            services.AddSingleton<IEconomyLedgerStore>(backend);
            services.AddSingleton<IEconomyAccountAdministrationStore>(backend);
            services.AddSingleton<ICommerceStore>(backend);
            services.AddSingleton<IRewardStore>(backend);
            services.AddSingleton<IDailyRewardPolicyStore, DailyPolicyStore>();
            services.AddSingleton<IGameResourceCatalog, StubCatalog>();
            services.AddSingleton<IRewardDeliveryPort, StubRewardDelivery>();
            services.AddSingleton<IPanelRuntimeStatus>(new StubRuntimeStatus(readiness));
            services.AddSingleton<QueryEconomyAccountsUseCase>();
            services.AddSingleton<QueryEconomyTransactionsUseCase>();
            services.AddSingleton<SetAccountFrozenUseCase>();
            services.AddSingleton<AdjustPlayerBalanceUseCase>();
            services.AddSingleton<SaveShopProductUseCase>();
            services.AddSingleton<GrantRewardUseCase>();
            services.AddSingleton<PurchaseProductUseCase>();
            services.AddSingleton<CreateRedeemCodeUseCase>();
            services.AddSingleton<RedeemCodeUseCase>();
            services.AddSingleton<SaveRewardPackageUseCase>();
            services.AddSingleton<SaveDailyRewardPolicyUseCase>();
            services.AddSingleton<PendingRewardReconciliationUseCase>();
            services.AddSingleton<ConfirmRewardGrantUseCase>();
            services.AddSingleton<RefundRewardGrantUseCase>();
            services.AddSingleton<CompensateRewardGrantUseCase>();
            services.AddSingleton<SaveAchievementDefinitionUseCase>();
            services.AddSingleton<SaveOnlineRewardRuleUseCase>();
            services.AddSingleton<ManualOnlineRewardGrantUseCase>();
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        public enum BackendMode
        {
            Normal,
            Unavailable,
            EconomyConcurrency,
            InsufficientFunds,
            EconomyIdempotency,
            PurchaseInsufficientFunds,
            RewardIdempotency,
            CommerceIdempotency
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class StubBackend :
            IEconomyLedgerStore,
            IEconomyAccountAdministrationStore,
            ICommerceStore,
            IRewardStore
        {
            private readonly BackendMode mode;

            public StubBackend(BackendMode mode) => this.mode = mode;

            public AccountSnapshot GetOrCreatePlayerAccount(
                string crossplatformId,
                string idempotencyKey,
                long openingAmount,
                DateTimeOffset occurredAtUtc) => Account(crossplatformId);

            public LedgerWriteResult Commit(LedgerTransactionDraft transaction)
            {
                if (mode == BackendMode.InsufficientFunds)
                    throw new EconomyInsufficientFundsException();
                if (mode == BackendMode.EconomyIdempotency)
                    throw new EconomyIdempotencyConflictException();
                return new LedgerWriteResult(
                    new LedgerTransactionSnapshot(
                        transaction.TransactionId,
                        transaction.Type,
                        transaction.IdempotencyKey,
                        transaction.OccurredAtUtc,
                        transaction.ActorKind,
                        transaction.ActorId,
                        transaction.RelatedCrossplatformId,
                        transaction.BusinessKind,
                        transaction.BusinessId,
                        transaction.CorrelationId,
                        transaction.Reason,
                        "Committed",
                        Array.Empty<LedgerEntrySnapshot>()),
                    new[] { Account(transaction.RelatedCrossplatformId ?? "EOS_1") });
            }

            public FundsReservationResult TryReserve(FundsReservationDraft reservation) =>
                throw new NotSupportedException();

            public LedgerWriteResult Capture(
                string reservationId,
                string transactionId,
                string idempotencyKey,
                DateTimeOffset occurredAtUtc) => throw new NotSupportedException();

            public bool Release(string reservationId, DateTimeOffset occurredAtUtc) => true;

            public AccountPage QueryAccounts(AccountKeysetQuery query)
            {
                if (mode == BackendMode.Unavailable) throw new InvalidOperationException("unavailable");
                return new AccountPage(
                    new[] { Account("EOS_1") },
                    new AccountKeyset(100, "account-1"));
            }

            public TransactionPage QueryTransactions(TransactionKeysetQuery query)
            {
                if (mode == BackendMode.Unavailable) throw new InvalidOperationException("unavailable");
                return new TransactionPage(
                    new[]
                    {
                        new LedgerTransactionSnapshot(
                            "transaction-1", "OwnerAdjustmentCredit", "request-1", Utc(),
                            "Owner", "owner-1", "EOS_1", "AccountAdjustment", "account-1",
                            "correlation-1", "support", "Committed",
                            Array.Empty<LedgerEntrySnapshot>())
                    },
                    new TransactionKeyset(Utc(), "transaction-1"));
            }

            public AccountSnapshot SetFrozen(
                string accountId,
                bool isFrozen,
                long expectedRowVersion,
                DateTimeOffset occurredAtUtc)
            {
                if (mode == BackendMode.EconomyConcurrency)
                    throw new EconomyConcurrencyException();
                if (string.Equals(accountId, "missing", StringComparison.Ordinal))
                    throw new EconomyAccountNotFoundException();
                return Account("EOS_1", isFrozen);
            }

            public ShopProductSnapshot SaveProduct(
                ShopProductDraft product,
                DateTimeOffset occurredAtUtc) => new ShopProductSnapshot(product, occurredAtUtc, occurredAtUtc, 0);

            public ShopProductSnapshot GetProduct(string productId)
            {
                if (string.Equals(productId, "missing", StringComparison.Ordinal))
                    throw new KeyNotFoundException();
                return Product(productId);
            }

            public PurchaseReservationResult ReservePurchase(PurchaseReservationRequest request)
            {
                if (mode == BackendMode.PurchaseInsufficientFunds)
                    return new PurchaseReservationResult(
                        PurchaseReservationStatus.InsufficientFunds,
                        null,
                        false);
                return new PurchaseReservationResult(
                    PurchaseReservationStatus.ProductDisabled,
                    null,
                    false);
            }

            public ShopPurchaseSnapshot GetPurchase(string purchaseId) => Purchase(purchaseId);
            public ShopPurchaseSnapshot? TryStartPurchaseDispatch(string purchaseId, DateTimeOffset occurredAtUtc) => null;
            public ShopPurchaseSnapshot ResolvePurchaseGrant(PurchaseGrantResolution resolution) => Purchase(resolution.PurchaseId);

            public RedeemCodeSnapshot SaveRedeemCode(
                RedeemCodeSecretDraft definition,
                DateTimeOffset occurredAtUtc) => new RedeemCodeSnapshot(
                    definition.CodeId,
                    "****-****-****-" + definition.LastFour,
                    definition.NormalizationVersion,
                    definition.RewardPackageId,
                    definition.Enabled,
                    definition.ValidFromUtc,
                    definition.ExpiresAtUtc,
                    definition.MaxRedemptions,
                    definition.PerPlayerLimit,
                    0,
                    occurredAtUtc,
                    occurredAtUtc,
                    0);

            public RedeemCodeSnapshot GetRedeemCode(string codeId) => new RedeemCodeSnapshot(
                "code-1", "****-****-****-ABCD", 1, "package-1", true,
                null, null, 5, 1, 0, Utc(), Utc(), 0);

            public RedemptionReservationResult ReserveRedemption(RedeemReservationRequest request) =>
                throw new NotSupportedException();
            public RedeemAttemptSnapshot ResolveRedemptionGrant(RedeemGrantResolution resolution) =>
                throw new NotSupportedException();

            public AchievementDefinitionSnapshot SaveAchievement(
                AchievementDefinitionDraft definition,
                DateTimeOffset occurredAtUtc) =>
                new AchievementDefinitionSnapshot(definition, occurredAtUtc, occurredAtUtc, 0);

            public AchievementProgressSnapshot GetAchievementProgress(
                string achievementId,
                string crossplatformId) => new AchievementProgressSnapshot(
                    achievementId, crossplatformId, 10, "eligibility-1", "grant-1",
                    Utc(), Utc(), 0);

            public IReadOnlyList<RewardEligibilitySnapshot> ObserveAchievement(
                ObserveAchievementCommand observation) => Array.Empty<RewardEligibilitySnapshot>();

            public OnlineRewardRuleSnapshot SaveOnlineRewardRule(
                OnlineRewardRuleDraft rule,
                DateTimeOffset occurredAtUtc) =>
                new OnlineRewardRuleSnapshot(rule, occurredAtUtc, occurredAtUtc, 0);

            public IReadOnlyList<RewardEligibilitySnapshot> EvaluateOnlineRewards(
                EvaluateOnlineRewardsCommand command) => Array.Empty<RewardEligibilitySnapshot>();

            public RewardEligibilitySnapshot ReserveManualOnlineReward(ManualOnlineRewardCommand command)
            {
                if (mode == BackendMode.CommerceIdempotency)
                    throw new CommerceIdempotencyConflictException();
                return Eligibility(RewardEligibilityState.Granted);
            }

            public RewardEligibilitySnapshot? TryReserveEligibilityGrant(
                string eligibilityId,
                DateTimeOffset occurredAtUtc) => null;

            public RewardEligibilitySnapshot ResolveEligibilityGrant(
                EligibilityGrantResolution resolution) => Eligibility(RewardEligibilityState.Granted);

            public IReadOnlyList<RewardEligibilitySnapshot> ListEligibilities(
                string ruleKind,
                string ruleId,
                string crossplatformId) => new[] { Eligibility(RewardEligibilityState.Granted) };

            public RewardPackageSnapshot SavePackage(
                RewardPackageDraft package,
                DateTimeOffset occurredAtUtc) => new RewardPackageSnapshot(
                    package.PackageId,
                    package.Name,
                    package.Description,
                    package.Enabled,
                    package.SortOrder,
                    occurredAtUtc,
                    occurredAtUtc,
                    0,
                    package.Entries.Select((entry, index) => new RewardPackageEntrySnapshot(
                        entry.EntryId,
                        index,
                        entry.Kind,
                        entry.ItemInternalName,
                        entry.ItemKind,
                        entry.Quantity,
                        entry.MinQuality,
                        entry.MaxQuality,
                        entry.CatalogVersion,
                        entry.CurrencyAmount,
                        entry.RegisteredAction)));

            public RewardPackageSnapshot GetPackage(string packageId) => Package(packageId);

            public GrantCreationResult GetOrCreateGrant(GrantOperationDraft operation)
            {
                if (mode == BackendMode.RewardIdempotency)
                    throw new RewardIdempotencyConflictException();
                return new GrantCreationResult(Grant(), false);
            }

            public GrantOperationSnapshot GetGrant(string operationId) => Grant(operationId);
            public bool TryStartDispatch(string operationId, long expectedRowVersion, DateTimeOffset occurredAtUtc) => true;
            public bool TryResolveDispatch(GrantDispatchResolution resolution) => true;
            public bool TryMarkPendingReconciliation(string operationId, string? errorCode, DateTimeOffset occurredAtUtc) => true;
            public IReadOnlyList<GrantOperationSnapshot> ListPendingReconciliation(int take) => new[] { Grant() };
            public bool TryConfirmReconciled(string operationId, long expectedRowVersion, string actorId, string correlationId, string? ledgerTransactionId, DateTimeOffset occurredAtUtc) => true;
            public bool TryMarkRefunded(string operationId, long expectedRowVersion, string refundLedgerTransactionId, string correlationId, DateTimeOffset occurredAtUtc) => true;
            public void RecordDeliveryOperation(string grantOperationId, string operationEntryId, string deliveryOperationId, DateTimeOffset occurredAtUtc) { }

            private static AccountSnapshot Account(string crossplatformId, bool frozen = false) =>
                new AccountSnapshot(
                    "account-1", EconomyAccountKind.Player, crossplatformId, true, frozen,
                    100, 0, Utc(), Utc(), 0);

            private static ShopProductSnapshot Product(string productId) => new ShopProductSnapshot(
                new ShopProductDraft(
                    productId, "Product", "Description", true, 10, null, 1, "package-1", 0),
                Utc(), Utc(), 0);

            private static ShopPurchaseSnapshot Purchase(string purchaseId) =>
                new ShopPurchaseSnapshot(
                    purchaseId, "product-1", "package-1", "EOS_1", 1, 10, 10,
                    PurchaseState.Completed, "purchase-1", "reservation-1", "transaction-1",
                    "grant-1", "correlation-1", null, Utc(), Utc(), Utc(), 0);

            private static RewardPackageSnapshot Package(string packageId) =>
                new RewardPackageSnapshot(
                    packageId, "Package", "Description", true, 0, Utc(), Utc(), 0,
                    new[]
                    {
                        new RewardPackageEntrySnapshot(
                            "entry-1", 0, RewardEntryKind.Currency, null, null, null,
                            null, null, null, 10, null)
                    });

            private static GrantOperationSnapshot Grant(string operationId = "grant-1") =>
                new GrantOperationSnapshot(
                    operationId, "package-1", "EOS_1", 1, "world-1",
                    GrantOperationState.PendingReconciliation, "grant-1", null, null, null,
                    "Owner", "owner-1", null, null, "correlation-1", null,
                    Utc(), Utc(), null, null, null, 0,
                    Array.Empty<GrantOperationEntrySnapshot>());

            private static RewardEligibilitySnapshot Eligibility(RewardEligibilityState state) =>
                new RewardEligibilitySnapshot(
                    "eligibility-1", "OnlineReward", "online-1", "package-1", "EOS_1",
                    "eligibility-key-1", state, "grant-1", "correlation-1", Utc(), Utc(),
                    Utc(), Utc(), 0);
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class StubCatalog : IGameResourceCatalog
        {
            public GameResourceCatalogReadResult Read() => GameResourceCatalogReadResult.Unavailable();
            public Task<GameResourceIconReadResult> ReadIconAsync(
                string catalogVersion,
                string resourceId,
                CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class DailyPolicyStore : IDailyRewardPolicyStore
        {
            private DailyRewardPolicySnapshot? current;

            public DailyRewardPolicySnapshot SaveDailyRewardPolicy(
                DailyRewardPolicyDraft policy,
                DateTimeOffset occurredAtUtc)
            {
                var version = current == null ? 0 : current.RowVersion + 1;
                current = new DailyRewardPolicySnapshot(policy, occurredAtUtc, occurredAtUtc, version);
                return current;
            }

            public DailyRewardPolicySnapshot GetDailyRewardPolicy(string ruleId) =>
                current != null && string.Equals(current.RuleId, ruleId, StringComparison.Ordinal)
                    ? current
                    : throw new DailyRewardPolicyUnavailableException();
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class StubRewardDelivery : IRewardDeliveryPort
        {
            public Task<RewardDeliveryResult> DeliverAsync(
                RewardDeliveryCommand command,
                CancellationToken cancellationToken) => Task.FromResult(
                    RewardDeliveryResult.Succeeded(Array.Empty<RewardDeliveryEntryResult>()));
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class StubRuntimeStatus : IPanelRuntimeStatus
        {
            public StubRuntimeStatus(GameReadinessState readiness) => GameReadiness = readiness;
            public ModHostState State => default;
            public GameReadinessState GameReadiness { get; }
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class HttpTestHost : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public HttpTestHost(ServiceProvider provider, HttpConfiguration configuration)
            {
                this.provider = provider;
                this.configuration = configuration;
                Client = new HttpClient(new HttpServer(configuration))
                {
                    BaseAddress = new Uri("http://localhost/")
                };
            }

            public HttpClient Client { get; }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Web")]

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;
            public PrincipalHandler(string? role) => this.role = role;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "owner-1"),
                            new Claim(ClaimTypes.Role, role)
                        },
                        "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                owin.Environment["owin.CallCancelled"] = cancellationToken;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }

        private static DateTimeOffset Utc() =>
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
    }
}
