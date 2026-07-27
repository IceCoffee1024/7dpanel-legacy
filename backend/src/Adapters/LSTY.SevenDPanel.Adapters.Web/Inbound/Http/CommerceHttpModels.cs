using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Economy;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class FreezeAccountHttpRequest
    {
        public bool IsFrozen { get; set; }
        public long ExpectedRowVersion { get; set; }
    }

    public sealed class AdjustBalanceHttpRequest
    {
        public LedgerSide PlayerSide { get; set; }
        public long Amount { get; set; }
        public string? ClientRequestKey { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class EconomyAccountHttpResponse
    {
        public EconomyAccountHttpResponse(AccountSnapshot account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            AccountId = account.AccountId;
            Kind = account.Kind;
            CrossplatformId = account.CrossplatformId;
            Enabled = account.Enabled;
            IsFrozen = account.IsFrozen;
            PostedBalance = account.PostedBalance;
            ReservedDebit = account.ReservedDebit;
            AvailableBalance = account.AvailableBalance;
            CreatedAtUtc = account.CreatedAtUtc;
            UpdatedAtUtc = account.UpdatedAtUtc;
            RowVersion = account.RowVersion;
        }

        public string AccountId { get; }
        public EconomyAccountKind Kind { get; }
        public string? CrossplatformId { get; }
        public bool Enabled { get; }
        public bool IsFrozen { get; }
        public long PostedBalance { get; }
        public long ReservedDebit { get; }
        public long AvailableBalance { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class EconomyAccountsPageHttpResponse
    {
        public EconomyAccountsPageHttpResponse(AccountPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            Accounts = page.Accounts.Select(account => new EconomyAccountHttpResponse(account)).ToArray();
            NextCursor = page.NextKeyset == null
                ? null
                : CommerceCursorCodec.Encode(page.NextKeyset);
        }

        public IReadOnlyList<EconomyAccountHttpResponse> Accounts { get; }
        public string? NextCursor { get; }
    }

    public sealed class LedgerEntryHttpResponse
    {
        public LedgerEntryHttpResponse(LedgerEntrySnapshot entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            EntryId = entry.EntryId;
            AccountId = entry.AccountId;
            Side = entry.Side;
            Amount = entry.Amount;
            BalanceAfter = entry.BalanceAfter;
        }

        public string EntryId { get; }
        public string AccountId { get; }
        public LedgerSide Side { get; }
        public long Amount { get; }
        public long BalanceAfter { get; }
    }

    public sealed class LedgerTransactionHttpResponse
    {
        public LedgerTransactionHttpResponse(LedgerTransactionSnapshot transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            TransactionId = transaction.TransactionId;
            Type = transaction.Type;
            OccurredAtUtc = transaction.OccurredAtUtc;
            ActorKind = transaction.ActorKind;
            ActorId = transaction.ActorId;
            RelatedCrossplatformId = transaction.RelatedCrossplatformId;
            BusinessKind = transaction.BusinessKind;
            BusinessId = transaction.BusinessId;
            CorrelationId = transaction.CorrelationId;
            Reason = transaction.Reason;
            Status = transaction.Status;
            Entries = transaction.Entries.Select(entry => new LedgerEntryHttpResponse(entry)).ToArray();
        }

        public string TransactionId { get; }
        public string Type { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? RelatedCrossplatformId { get; }
        public string? BusinessKind { get; }
        public string? BusinessId { get; }
        public string? CorrelationId { get; }
        public string? Reason { get; }
        public string Status { get; }
        public IReadOnlyList<LedgerEntryHttpResponse> Entries { get; }
    }

    public sealed class EconomyTransactionsPageHttpResponse
    {
        public EconomyTransactionsPageHttpResponse(TransactionPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            Transactions = page.Transactions
                .Select(transaction => new LedgerTransactionHttpResponse(transaction))
                .ToArray();
            NextCursor = page.NextKeyset == null
                ? null
                : CommerceCursorCodec.Encode(page.NextKeyset);
        }

        public IReadOnlyList<LedgerTransactionHttpResponse> Transactions { get; }
        public string? NextCursor { get; }
    }

    public sealed class ShopProductUpsertHttpRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public long PriceAmount { get; set; }
        public long? StockRemaining { get; set; }
        public int? PerPlayerLimit { get; set; }
        public string? RewardPackageId { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ShopProductHttpResponse
    {
        public ShopProductHttpResponse(ShopProductSnapshot product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            ProductId = product.ProductId;
            Name = product.Name;
            Description = product.Description;
            Enabled = product.Enabled;
            PriceAmount = product.PriceAmount;
            StockRemaining = product.StockRemaining;
            PerPlayerLimit = product.PerPlayerLimit;
            RewardPackageId = product.RewardPackageId;
            SortOrder = product.SortOrder;
            CreatedAtUtc = product.CreatedAtUtc;
            UpdatedAtUtc = product.UpdatedAtUtc;
            RowVersion = product.RowVersion;
        }

        public string ProductId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public long PriceAmount { get; }
        public long? StockRemaining { get; }
        public int? PerPlayerLimit { get; }
        public string RewardPackageId { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class PurchaseProductHttpRequest
    {
        public string? CrossplatformId { get; set; }
        public int ExpectedEntityId { get; set; }
        public string? ExpectedWorldId { get; set; }
        public int Quantity { get; set; }
        public string? ClientRequestKey { get; set; }
    }

    public sealed class ShopPurchaseHttpResponse
    {
        public ShopPurchaseHttpResponse(ShopPurchaseSnapshot purchase)
        {
            if (purchase == null) throw new ArgumentNullException(nameof(purchase));
            PurchaseId = purchase.PurchaseId;
            ProductId = purchase.ProductId;
            RewardPackageId = purchase.RewardPackageId;
            CrossplatformId = purchase.CrossplatformId;
            Quantity = purchase.Quantity;
            UnitPrice = purchase.UnitPrice;
            TotalAmount = purchase.TotalAmount;
            State = purchase.State;
            ReservationId = purchase.ReservationId;
            CapturedTransactionId = purchase.CapturedTransactionId;
            GrantOperationId = purchase.GrantOperationId;
            CorrelationId = purchase.CorrelationId;
            ErrorCode = purchase.ErrorCode;
            CreatedAtUtc = purchase.CreatedAtUtc;
            UpdatedAtUtc = purchase.UpdatedAtUtc;
            CompletedAtUtc = purchase.CompletedAtUtc;
            RowVersion = purchase.RowVersion;
        }

        public string PurchaseId { get; }
        public string ProductId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public int Quantity { get; }
        public long UnitPrice { get; }
        public long TotalAmount { get; }
        public PurchaseState State { get; }
        public string? ReservationId { get; }
        public string? CapturedTransactionId { get; }
        public string? GrantOperationId { get; }
        public string? CorrelationId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class PurchaseProductHttpResponse
    {
        public PurchaseProductHttpResponse(PurchaseProductResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            Status = result.Status;
            Purchase = result.Purchase == null ? null : new ShopPurchaseHttpResponse(result.Purchase);
        }

        public PurchaseRequestStatus Status { get; }
        public ShopPurchaseHttpResponse? Purchase { get; }
    }

    public sealed class CreateRedeemCodeHttpRequest
    {
        public string? RewardPackageId { get; set; }
        public bool Enabled { get; set; }
        public DateTimeOffset? ValidFromUtc { get; set; }
        public DateTimeOffset? ExpiresAtUtc { get; set; }
        public int? MaxRedemptions { get; set; }
        public int? PerPlayerLimit { get; set; }
    }

    public sealed class RedeemCodeHttpResponse
    {
        public RedeemCodeHttpResponse(RedeemCodeSnapshot code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            CodeId = code.CodeId;
            MaskedCode = code.MaskedCode;
            RewardPackageId = code.RewardPackageId;
            Enabled = code.Enabled;
            ValidFromUtc = code.ValidFromUtc;
            ExpiresAtUtc = code.ExpiresAtUtc;
            MaxRedemptions = code.MaxRedemptions;
            PerPlayerLimit = code.PerPlayerLimit;
            RedemptionCount = code.RedemptionCount;
            CreatedAtUtc = code.CreatedAtUtc;
            UpdatedAtUtc = code.UpdatedAtUtc;
            RowVersion = code.RowVersion;
        }

        public string CodeId { get; }
        public string MaskedCode { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public DateTimeOffset? ValidFromUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
        public int? MaxRedemptions { get; }
        public int? PerPlayerLimit { get; }
        public int RedemptionCount { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class GeneratedRedeemCodeHttpResponse
    {
        public GeneratedRedeemCodeHttpResponse(GeneratedRedeemCode generated)
        {
            if (generated == null) throw new ArgumentNullException(nameof(generated));
            Code = generated.PlaintextCode;
            Definition = new RedeemCodeHttpResponse(generated.Definition);
        }

        public string Code { get; }
        public RedeemCodeHttpResponse Definition { get; }
    }

    public sealed class RedeemHttpRequest
    {
        public string? Code { get; set; }
        public string? CrossplatformId { get; set; }
        public int ExpectedEntityId { get; set; }
        public string? ExpectedWorldId { get; set; }
    }

    public sealed class RedeemAttemptHttpResponse
    {
        public RedeemAttemptHttpResponse(RedeemAttemptSnapshot attempt)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            AttemptId = attempt.AttemptId;
            CodeId = attempt.CodeId;
            RewardPackageId = attempt.RewardPackageId;
            CrossplatformId = attempt.CrossplatformId;
            State = attempt.State;
            ResultCode = attempt.ResultCode;
            GrantOperationId = attempt.GrantOperationId;
            CorrelationId = attempt.CorrelationId;
            AttemptedAtUtc = attempt.AttemptedAtUtc;
        }

        public string AttemptId { get; }
        public string CodeId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public RedeemAttemptState State { get; }
        public string? ResultCode { get; }
        public string? GrantOperationId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset AttemptedAtUtc { get; }
    }

    public sealed class RedeemHttpResponse
    {
        public RedeemHttpResponse(RedeemCodeResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            Status = result.Status;
            Attempt = result.Attempt == null ? null : new RedeemAttemptHttpResponse(result.Attempt);
        }

        public RedeemRequestStatus Status { get; }
        public RedeemAttemptHttpResponse? Attempt { get; }
    }

    public static class CommerceCursorCodec
    {
        public static string Encode(AccountKeyset keyset)
        {
            if (keyset == null) throw new ArgumentNullException(nameof(keyset));
            return Encode("A", keyset.PostedBalance.ToString(CultureInfo.InvariantCulture), keyset.AccountId);
        }

        public static string Encode(TransactionKeyset keyset)
        {
            if (keyset == null) throw new ArgumentNullException(nameof(keyset));
            return Encode(
                "T",
                keyset.OccurredAtUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                keyset.TransactionId);
        }

        public static bool TryDecodeAccount(string? value, out AccountKeyset? keyset)
        {
            keyset = null;
            if (!TryDecode(value, "A", out var number, out var identifier)) return false;
            keyset = new AccountKeyset(number, identifier!);
            return true;
        }

        public static bool TryDecodeTransaction(string? value, out TransactionKeyset? keyset)
        {
            keyset = null;
            if (!TryDecode(value, "T", out var number, out var identifier)) return false;
            try
            {
                keyset = new TransactionKeyset(
                    DateTimeOffset.FromUnixTimeMilliseconds(number),
                    identifier!);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static string Encode(string kind, string number, string identifier)
        {
            var payload = string.Join("\n", new[]
            {
                kind,
                number,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(identifier))
            });
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool TryDecode(
            string? value,
            string expectedKind,
            out long number,
            out string? identifier)
        {
            number = 0;
            identifier = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                var normalized = value!.Replace('-', '+').Replace('_', '/');
                switch (normalized.Length % 4)
                {
                    case 0: break;
                    case 2: normalized += "=="; break;
                    case 3: normalized += "="; break;
                    default: return false;
                }
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(normalized))
                    .Split(new[] { '\n' });
                if (parts.Length != 3 ||
                    !string.Equals(parts[0], expectedKind, StringComparison.Ordinal) ||
                    !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    return false;
                }
                identifier = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
                return !string.IsNullOrWhiteSpace(identifier);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    internal static class CommerceRewardHttpSupport
    {
        internal static string RequireText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.");
            return value!.Trim();
        }

        internal static string RequireActor(ApiController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            var actor = (controller.User?.Identity as ClaimsIdentity)?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("authentication_required");
            return actor!;
        }

        internal static string Correlation(ApiController controller) =>
            Errors.ApiProblemDetailsFactory.GetTraceId(controller.Request);

        internal static string StableId(string prefix, string clientRequestKey)
        {
            var key = RequireText(clientRequestKey);
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(prefix + "\n" + key));
            var builder = new StringBuilder(prefix.Length + 1 + 32);
            builder.Append(prefix).Append('-');
            for (var index = 0; index < 16; index++)
                builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
