using System.Collections.Generic;
using LSTY.SevenDPanel.Application.Mods;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ModManagementUseCaseTests
    {
        [Fact]
        public void List_keeps_runtime_and_next_start_states_separate()
        {
            var catalog = new StubCatalog(new ModDiskEntry(
                "Example", "Example", "Example Mod", "Author", "1.0", null, null, false, false));
            var loaded = new StubLoadedQuery(new LoadedModSnapshot(true, new[] { "Example" }));

            var mod = Assert.Single(new ListModsUseCase(catalog, loaded).Execute());

            Assert.True(mod.IsLoadedNow);
            Assert.False(mod.IsEnabledNextStart);
        }

        [Fact]
        public void List_reports_current_state_as_unknown_when_runtime_snapshot_is_unavailable()
        {
            var catalog = new StubCatalog(new ModDiskEntry(
                "Example", "Example", "Example Mod", "Author", "1.0", null, null, true, false));
            var loaded = new StubLoadedQuery(LoadedModSnapshot.Unavailable());

            var mod = Assert.Single(new ListModsUseCase(catalog, loaded).Execute());

            Assert.Null(mod.IsLoadedNow);
        }

        [Fact]
        public void State_change_delegates_only_the_directory_identifier_and_target_state()
        {
            var catalog = new StubCatalog();
            catalog.ChangeResult = ModStateChangeResult.Changed();

            var result = new SetModStateUseCase(catalog).Execute("Example", false);

            Assert.Equal(ModStateChangeStatus.Changed, result.Status);
            Assert.Equal("Example", catalog.ChangedDirectoryId);
            Assert.False(catalog.ChangedEnabled);
        }

        private sealed class StubCatalog : IModCatalog
        {
            private readonly IReadOnlyList<ModDiskEntry> entries;

            public StubCatalog(params ModDiskEntry[] entries)
            {
                this.entries = entries;
            }

            public ModStateChangeResult ChangeResult { get; set; } = ModStateChangeResult.NotFound();
            public string? ChangedDirectoryId { get; private set; }
            public bool? ChangedEnabled { get; private set; }

            public IReadOnlyList<ModDiskEntry> List() => entries;

            public ModStateChangeResult SetEnabled(string directoryId, bool enabled)
            {
                ChangedDirectoryId = directoryId;
                ChangedEnabled = enabled;
                return ChangeResult;
            }
        }

        private sealed class StubLoadedQuery : ILoadedModQuery
        {
            private readonly LoadedModSnapshot snapshot;

            public StubLoadedQuery(LoadedModSnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public LoadedModSnapshot GetLoadedNames() => snapshot;
        }
    }
}
