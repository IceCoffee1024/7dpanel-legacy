using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class OverviewAttention
    {
        public OverviewAttention(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("An attention code is required.", nameof(code));

            Code = code;
        }

        public string Code { get; }
    }
}
