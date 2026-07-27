using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Domain.Economy
{
    public enum LedgerSide
    {
        Debit,
        Credit
    }

    public readonly struct LedgerEntryAmount
    {
        public LedgerEntryAmount(LedgerSide side, long amount)
        {
            if (!Enum.IsDefined(typeof(LedgerSide), side))
                throw new ArgumentOutOfRangeException(nameof(side));
            LedgerRules.ValidateAmount(amount);
            Side = side;
            Amount = amount;
        }

        public LedgerSide Side { get; }

        public long Amount { get; }
    }

    public static class LedgerRules
    {
        public static void ValidateAmount(long amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
        }

        public static bool IsBalanced(IEnumerable<LedgerEntryAmount> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            long debit = 0;
            long credit = 0;
            var hasEntry = false;
            checked
            {
                foreach (var entry in entries)
                {
                    hasEntry = true;
                    ValidateAmount(entry.Amount);
                    if (!Enum.IsDefined(typeof(LedgerSide), entry.Side))
                        throw new ArgumentOutOfRangeException(nameof(entries));
                    if (entry.Side == LedgerSide.Debit)
                        debit += entry.Amount;
                    else
                        credit += entry.Amount;
                }
            }

            return hasEntry && debit == credit;
        }

        public static long Apply(
            long postedBalance,
            LedgerSide side,
            long amount,
            bool isSystemAccount)
        {
            if (!Enum.IsDefined(typeof(LedgerSide), side))
                throw new ArgumentOutOfRangeException(nameof(side));
            ValidateAmount(amount);
            if (!isSystemAccount && postedBalance < 0)
                throw new InvalidOperationException("Player account balance cannot be negative.");

            var next = checked(side == LedgerSide.Credit
                ? postedBalance + amount
                : postedBalance - amount);
            if (!isSystemAccount && next < 0)
                throw new InvalidOperationException("Player account cannot be overdrawn.");
            return next;
        }
    }
}
