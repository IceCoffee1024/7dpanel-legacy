using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerEvidenceScalarAdapterTests
    {
        [Fact]
        public void Mod_identifiers_merge_functional_and_cosmetic_sources_stably()
        {
            var mods = SevenDaysPlayerEvidenceSnapshotReader.CopyModInternalNames(
                new string?[] { " modGrip ", null, "modShared" },
                new string?[] { "modDye", "modShared", " " });

            Assert.Equal(
                new[] { "modGrip", "modShared", "modDye" },
                mods);
        }
    }
}
