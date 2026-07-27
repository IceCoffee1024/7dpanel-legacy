using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1")]
    public sealed class CommerceController : ApiController
    {
        private readonly QueryEconomyAccountsUseCase queryAccounts;
        private readonly QueryEconomyTransactionsUseCase queryTransactions;
        private readonly SetAccountFrozenUseCase setFrozen;
        private readonly AdjustPlayerBalanceUseCase adjustBalance;
        private readonly ICommerceStore store;
        private readonly SaveShopProductUseCase saveProduct;
        private readonly PurchaseProductUseCase purchaseProduct;
        private readonly CreateRedeemCodeUseCase createRedeemCode;
        private readonly RedeemCodeUseCase redeemCode;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public CommerceController(
            QueryEconomyAccountsUseCase queryAccounts,
            QueryEconomyTransactionsUseCase queryTransactions,
            SetAccountFrozenUseCase setFrozen,
            AdjustPlayerBalanceUseCase adjustBalance,
            ICommerceStore store,
            SaveShopProductUseCase saveProduct,
            PurchaseProductUseCase purchaseProduct,
            CreateRedeemCodeUseCase createRedeemCode,
            RedeemCodeUseCase redeemCode,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.queryAccounts = queryAccounts ?? throw new ArgumentNullException(nameof(queryAccounts));
            this.queryTransactions = queryTransactions ?? throw new ArgumentNullException(nameof(queryTransactions));
            this.setFrozen = setFrozen ?? throw new ArgumentNullException(nameof(setFrozen));
            this.adjustBalance = adjustBalance ?? throw new ArgumentNullException(nameof(adjustBalance));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.saveProduct = saveProduct ?? throw new ArgumentNullException(nameof(saveProduct));
            this.purchaseProduct = purchaseProduct ?? throw new ArgumentNullException(nameof(purchaseProduct));
            this.createRedeemCode = createRedeemCode ?? throw new ArgumentNullException(nameof(createRedeemCode));
            this.redeemCode = redeemCode ?? throw new ArgumentNullException(nameof(redeemCode));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpGet]
        [Route("economy/accounts")]
        [ResponseType(typeof(EconomyAccountsPageHttpResponse))]
        public HttpResponseMessage GetAccounts(
            string? limit = null,
            string? includeSystem = null,
            string? search = null,
            string? enabled = null,
            string? frozen = null,
            string? cursor = null)
        {
            AccountKeyset? keyset = null;
            if (!TryLimit(limit, out var pageSize) ||
                !TryBoolean(includeSystem, true, out var includeSystemValue) ||
                !TryOptionalBoolean(enabled, out var enabledValue) ||
                !TryOptionalBoolean(frozen, out var frozenValue) ||
                !TryAccountCursor(cursor, out keyset))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    cursor != null && keyset == null
                        ? "invalid_economy_cursor"
                        : "invalid_economy_query",
                    "The economy account query is invalid.");
            }

            try
            {
                var page = queryAccounts.Execute(new AccountKeysetQuery(
                    pageSize,
                    includeSystemValue,
                    search,
                    enabledValue,
                    frozenValue,
                    keyset));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new EconomyAccountsPageHttpResponse(page));
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_economy_query", "The economy account query is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "economy_unavailable", "Economy accounts are unavailable.");
            }
        }

        [HttpGet]
        [Route("economy/leaderboard")]
        [ResponseType(typeof(EconomyAccountsPageHttpResponse))]
        public HttpResponseMessage GetLeaderboard(string? limit = null, string? cursor = null)
        {
            AccountKeyset? keyset = null;
            if (!TryLimit(limit, out var pageSize) || !TryAccountCursor(cursor, out keyset))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    cursor != null && keyset == null
                        ? "invalid_economy_cursor"
                        : "invalid_economy_query",
                    "The economy leaderboard query is invalid.");
            }

            try
            {
                var page = queryAccounts.Execute(new AccountKeysetQuery(
                    pageSize,
                    includeSystem: false,
                    keyset: keyset));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new EconomyAccountsPageHttpResponse(page));
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_economy_query", "The economy leaderboard query is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "economy_unavailable", "The economy leaderboard is unavailable.");
            }
        }

        [HttpGet]
        [Route("economy/transactions")]
        [ResponseType(typeof(EconomyTransactionsPageHttpResponse))]
        public HttpResponseMessage GetTransactions(
            string? limit = null,
            string? relatedCrossplatformId = null,
            string? accountId = null,
            string? type = null,
            string? businessKind = null,
            string? cursor = null)
        {
            TransactionKeyset? keyset = null;
            if (!TryLimit(limit, out var pageSize) || !TryTransactionCursor(cursor, out keyset))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    cursor != null && keyset == null
                        ? "invalid_economy_cursor"
                        : "invalid_economy_query",
                    "The economy transaction query is invalid.");
            }

            try
            {
                var page = queryTransactions.Execute(new TransactionKeysetQuery(
                    pageSize,
                    relatedCrossplatformId,
                    accountId,
                    type,
                    businessKind,
                    keyset));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new EconomyTransactionsPageHttpResponse(page));
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_economy_query", "The economy transaction query is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "economy_unavailable", "Economy transactions are unavailable.");
            }
        }

        [HttpPost]
        [Route("economy/accounts/{accountId}/freeze")]
        [ResponseType(typeof(EconomyAccountHttpResponse))]
        public HttpResponseMessage FreezeAccount(string accountId, FreezeAccountHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var account = setFrozen.Execute(new SetAccountFrozenCommand(
                    accountId,
                    body.IsFrozen,
                    body.ExpectedRowVersion,
                    DateTimeOffset.UtcNow));
                return Request.CreateResponse(HttpStatusCode.OK, new EconomyAccountHttpResponse(account));
            }
            catch (EconomyAccountNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "economy_account_not_found", "The economy account was not found.");
            }
            catch (EconomyConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "economy_concurrency_conflict", "The economy account changed before the request completed.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_economy_request", "The economy request is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "economy_unavailable", "The economy account is unavailable.");
            }
        }

        [HttpPost]
        [Route("economy/accounts/{crossplatformId}/adjust")]
        [ResponseType(typeof(LedgerTransactionHttpResponse))]
        public HttpResponseMessage AdjustBalance(string crossplatformId, AdjustBalanceHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var actor = CommerceRewardHttpSupport.RequireActor(this);
                var clientRequestKey = CommerceRewardHttpSupport.RequireText(body.ClientRequestKey);
                var result = adjustBalance.Execute(new AdjustPlayerBalanceCommand(
                    CommerceRewardHttpSupport.StableId("adjust", clientRequestKey),
                    clientRequestKey,
                    crossplatformId,
                    body.PlayerSide,
                    body.Amount,
                    actor,
                    DateTimeOffset.UtcNow,
                    CommerceRewardHttpSupport.Correlation(this),
                    CommerceRewardHttpSupport.RequireText(body.Reason)));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new LedgerTransactionHttpResponse(result.Transaction));
            }
            catch (EconomyAccountNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "economy_account_not_found", "The player economy account was not found.");
            }
            catch (EconomyInsufficientFundsException)
            {
                return Problem(HttpStatusCode.Conflict, "economy_insufficient_funds", "The player account has insufficient available funds.");
            }
            catch (EconomyIdempotencyConflictException)
            {
                return Problem(HttpStatusCode.Conflict, "economy_idempotency_conflict", "The client request key was already used for a different adjustment.");
            }
            catch (EconomyConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "economy_concurrency_conflict", "The economy account changed before the request completed.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_economy_request", "The economy request is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "economy_unavailable", "The economy adjustment is unavailable.");
            }
        }

        [HttpGet]
        [Route("shop/products/{productId}")]
        [ResponseType(typeof(ShopProductHttpResponse))]
        public HttpResponseMessage GetProduct(string productId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new ShopProductHttpResponse(store.GetProduct(productId)));
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "shop_product_not_found", "The shop product was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_shop_product", "The shop product identifier is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The shop product is unavailable.");
            }
        }

        [HttpPut]
        [Route("shop/products/{productId}")]
        [ResponseType(typeof(ShopProductHttpResponse))]
        public HttpResponseMessage PutProduct(string productId, ShopProductUpsertHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var product = saveProduct.Execute(new ShopProductDraft(
                    productId,
                    CommerceRewardHttpSupport.RequireText(body.Name),
                    body.Description ?? string.Empty,
                    body.Enabled,
                    body.PriceAmount,
                    body.StockRemaining,
                    body.PerPlayerLimit,
                    CommerceRewardHttpSupport.RequireText(body.RewardPackageId),
                    body.SortOrder));
                return Request.CreateResponse(HttpStatusCode.OK, new ShopProductHttpResponse(product));
            }
            catch (CommerceConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_concurrency_conflict", "The shop product changed before the request completed.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found", "The reward package was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_shop_product", "The shop product is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The shop product could not be saved.");
            }
        }

        [HttpGet]
        [Route("shop/purchases/{purchaseId}")]
        [ResponseType(typeof(ShopPurchaseHttpResponse))]
        public HttpResponseMessage GetPurchase(string purchaseId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new ShopPurchaseHttpResponse(store.GetPurchase(purchaseId)));
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "shop_purchase_not_found", "The shop purchase was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_shop_purchase", "The shop purchase identifier is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The shop purchase is unavailable.");
            }
        }

        [HttpPost]
        [Route("shop/products/{productId}/purchases")]
        [ResponseType(typeof(PurchaseProductHttpResponse))]
        public async Task<HttpResponseMessage> Purchase(
            string productId,
            PurchaseProductHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return GameNotReady();
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var result = await purchaseProduct.ExecuteAsync(
                    new PurchaseProductCommand(
                        productId,
                        CommerceRewardHttpSupport.RequireText(body.CrossplatformId),
                        body.ExpectedEntityId,
                        CommerceRewardHttpSupport.RequireText(body.ExpectedWorldId),
                        body.Quantity,
                        CommerceRewardHttpSupport.RequireText(body.ClientRequestKey),
                        CommerceRewardHttpSupport.Correlation(this)),
                    cancellationToken).ConfigureAwait(false);
                return PurchaseResponse(result);
            }
            catch (CommerceIdempotencyConflictException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_idempotency_conflict", "The client request key was already used for a different purchase.");
            }
            catch (CommerceConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_concurrency_conflict", "The shop changed before the purchase completed.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_purchase_request", "The purchase request is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The purchase service is unavailable.");
            }
        }

        [HttpGet]
        [Route("redeem-codes/{codeId}")]
        [ResponseType(typeof(RedeemCodeHttpResponse))]
        public HttpResponseMessage GetRedeemCode(string codeId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new RedeemCodeHttpResponse(store.GetRedeemCode(codeId)));
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "redeem_code_not_found", "The redeem code was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_redeem_code", "The redeem code identifier is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The redeem code is unavailable.");
            }
        }

        [HttpPost]
        [Route("redeem-codes")]
        [ResponseType(typeof(GeneratedRedeemCodeHttpResponse))]
        public HttpResponseMessage CreateRedeemCode(CreateRedeemCodeHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var generated = createRedeemCode.Execute(new CreateRedeemCodeCommand(
                    CommerceRewardHttpSupport.RequireText(body.RewardPackageId),
                    body.Enabled,
                    body.ValidFromUtc,
                    body.ExpiresAtUtc,
                    body.MaxRedemptions,
                    body.PerPlayerLimit));
                var response = Request.CreateResponse(
                    HttpStatusCode.Created,
                    new GeneratedRedeemCodeHttpResponse(generated));
                response.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true
                };
                response.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));
                return response;
            }
            catch (CommerceConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_concurrency_conflict", "The redeem-code configuration changed before the request completed.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found", "The reward package was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_redeem_code", "The redeem-code configuration is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The redeem code could not be created.");
            }
        }

        [HttpPost]
        [Route("redemptions")]
        [ResponseType(typeof(RedeemHttpResponse))]
        public async Task<HttpResponseMessage> Redeem(
            RedeemHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return GameNotReady();
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var result = await redeemCode.ExecuteAsync(
                    new RedeemCodeCommand(
                        CommerceRewardHttpSupport.RequireText(body.Code),
                        CommerceRewardHttpSupport.RequireText(body.CrossplatformId),
                        body.ExpectedEntityId,
                        CommerceRewardHttpSupport.RequireText(body.ExpectedWorldId),
                        CommerceRewardHttpSupport.Correlation(this)),
                    cancellationToken).ConfigureAwait(false);
                return RedemptionResponse(result);
            }
            catch (CommerceIdempotencyConflictException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_idempotency_conflict", "The redemption already has a different authoritative result.");
            }
            catch (CommerceConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_concurrency_conflict", "The redeem code changed before the request completed.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_redemption_request", "The redemption request is invalid.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "commerce_unavailable", "The redemption service is unavailable.");
            }
        }

        private HttpResponseMessage PurchaseResponse(PurchaseProductResult result)
        {
            switch (result.Status)
            {
                case PurchaseRequestStatus.Completed:
                    return Request.CreateResponse(HttpStatusCode.OK, new PurchaseProductHttpResponse(result));
                case PurchaseRequestStatus.Reserved:
                case PurchaseRequestStatus.PendingReconciliation:
                    return Request.CreateResponse(HttpStatusCode.Accepted, new PurchaseProductHttpResponse(result));
                case PurchaseRequestStatus.InsufficientFunds:
                    return Problem(HttpStatusCode.Conflict, "economy_insufficient_funds", "The player account has insufficient available funds.");
                case PurchaseRequestStatus.ProductDisabled:
                    return Problem(HttpStatusCode.Conflict, "shop_product_disabled", "The shop product is disabled.");
                case PurchaseRequestStatus.AccountDisabled:
                    return Problem(HttpStatusCode.Conflict, "economy_account_disabled", "The player economy account is disabled.");
                case PurchaseRequestStatus.AccountFrozen:
                    return Problem(HttpStatusCode.Conflict, "economy_account_frozen", "The player economy account is frozen.");
                case PurchaseRequestStatus.OutOfStock:
                    return Problem(HttpStatusCode.Conflict, "shop_product_out_of_stock", "The shop product is out of stock.");
                case PurchaseRequestStatus.PlayerLimitReached:
                    return Problem(HttpStatusCode.Conflict, "shop_player_limit_reached", "The per-player purchase limit was reached.");
                default:
                    return Problem(HttpStatusCode.Conflict, "shop_purchase_failed", "The purchase did not complete.");
            }
        }

        private HttpResponseMessage RedemptionResponse(RedeemCodeResult result)
        {
            switch (result.Status)
            {
                case RedeemRequestStatus.Succeeded:
                    return Request.CreateResponse(HttpStatusCode.OK, new RedeemHttpResponse(result));
                case RedeemRequestStatus.Pending:
                case RedeemRequestStatus.PendingReconciliation:
                    return Request.CreateResponse(HttpStatusCode.Accepted, new RedeemHttpResponse(result));
                case RedeemRequestStatus.InvalidCode:
                    return Problem(HttpStatusCode.BadRequest, "redeem_code_invalid", "The redeem code is invalid.");
                case RedeemRequestStatus.Disabled:
                    return Problem(HttpStatusCode.Conflict, "redeem_code_disabled", "The redeem code is disabled.");
                case RedeemRequestStatus.NotYetValid:
                    return Problem(HttpStatusCode.Conflict, "redeem_code_not_yet_valid", "The redeem code is not yet valid.");
                case RedeemRequestStatus.Expired:
                    return Problem(HttpStatusCode.Conflict, "redeem_code_expired", "The redeem code has expired.");
                case RedeemRequestStatus.GlobalLimitReached:
                    return Problem(HttpStatusCode.Conflict, "redeem_code_global_limit_reached", "The redeem code has reached its global limit.");
                case RedeemRequestStatus.PlayerLimitReached:
                    return Problem(HttpStatusCode.Conflict, "redeem_code_player_limit_reached", "The player has reached this redeem code's limit.");
                default:
                    return Problem(HttpStatusCode.Conflict, "redemption_failed", "The redemption did not complete.");
            }
        }

        private static bool TryLimit(string? value, out int limit)
        {
            limit = 50;
            return value == null ||
                value.Length > 0 &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) &&
                limit >= 1 &&
                limit <= 200;
        }

        private static bool TryBoolean(string? value, bool defaultValue, out bool result)
        {
            result = defaultValue;
            return value == null || bool.TryParse(value, out result);
        }

        private static bool TryOptionalBoolean(string? value, out bool? result)
        {
            result = null;
            if (value == null) return true;
            if (!bool.TryParse(value, out var parsed)) return false;
            result = parsed;
            return true;
        }

        private static bool TryAccountCursor(string? value, out AccountKeyset? keyset)
        {
            keyset = null;
            return value == null || CommerceCursorCodec.TryDecodeAccount(value, out keyset);
        }

        private static bool TryTransactionCursor(string? value, out TransactionKeyset? keyset)
        {
            keyset = null;
            return value == null || CommerceCursorCodec.TryDecodeTransaction(value, out keyset);
        }

        private HttpResponseMessage GameNotReady() =>
            Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready for commerce delivery.");

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
