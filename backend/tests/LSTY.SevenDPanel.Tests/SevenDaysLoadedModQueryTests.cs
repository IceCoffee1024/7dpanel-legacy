using System;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Mods;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysLoadedModQueryTests
    {
        [Fact]
        public void Copies_available_names_without_exposing_mutable_runtime_state()
        {
            var names = new[] { "Panel", "Example" };
            var query = new SevenDaysLoadedModQuery(() => (true, names));

            var result = query.GetLoadedNames();
            names[0] = "Changed";

            Assert.True(result.Available);
            Assert.Equal(new[] { "Panel", "Example" }, result.Names);
        }

        [Fact]
        public void Runtime_failure_is_reported_as_unavailable()
        {
            var query = new SevenDaysLoadedModQuery(
                () => throw new InvalidOperationException("game not ready"));

            Assert.False(query.GetLoadedNames().Available);
        }
    }
}
