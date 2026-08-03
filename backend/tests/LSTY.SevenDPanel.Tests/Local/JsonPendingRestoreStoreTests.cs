using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Local")]
    public sealed class JsonPendingRestoreStoreTests
    {
        [Fact]
        public void Marker_round_trips_the_versioned_immutable_snapshot_and_leaves_no_temporary_file()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            var marker = CreateMarker();

            store.CreateMarker(marker);

            Assert.Equal(marker, store.ReadMarker());
            var stateDirectory = StateDirectory(directories);
            Assert.Empty(Directory.EnumerateFiles(stateDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
            var json = File.ReadAllText(MarkerPath(directories));
            Assert.DoesNotContain("connectionString", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("shell", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(directories.Root, json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_second_marker_is_rejected_without_replacing_the_first()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            var first = CreateMarker();
            store.CreateMarker(first);

            var error = Assert.Throws<RestoreStateException>(() =>
                store.CreateMarker(CreateMarker(Guid.NewGuid())));

            Assert.Equal(JsonPendingRestoreStore.MarkerAlreadyExistsError, error.ErrorCode);
            Assert.Equal(first, store.ReadMarker());
        }

        [Theory]
        [InlineData("\"version\":1", "\"version\":2")]
        [InlineData("\"backupKind\":\"PanelDatabase\"", "\"backupKind\":\"Unknown\"")]
        [InlineData("\"relativeResourceId\":\"panel-backup.zip\"", "\"relativeResourceId\":\"C:/panel-backup.zip\"")]
        [InlineData("\"relativeResourceId\":\"panel-backup.zip\"", "\"relativeResourceId\":\"nested/panel-backup.zip\"")]
        [InlineData("\"relativeResourceId\":\"panel-backup.zip\"", "\"relativeResourceId\":\"nested\\\\panel-backup.zip\"")]
        [InlineData("\"relativeResourceId\":\"panel-backup.zip\"", "\"relativeResourceId\":\"panel..backup.zip\"")]
        [InlineData("\"stage\":\"Prepared\"", "\"stage\":\"Unknown\"")]
        [InlineData("\"jobKind\":\"Restore\"", "\"jobKind\":\"WorldBackup\"")]
        [InlineData("\"jobStatus\":\"PendingRestart\"", "\"jobStatus\":\"Running\"")]
        public void Strict_marker_parser_rejects_unknown_versions_enums_and_non_opaque_resource_ids(
            string oldValue,
            string newValue)
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            store.CreateMarker(CreateMarker());
            var path = MarkerPath(directories);
            File.WriteAllText(path, File.ReadAllText(path).Replace(oldValue, newValue));

            var error = Assert.Throws<RestoreStateException>(() => store.ReadMarker());

            Assert.Equal(JsonPendingRestoreStore.MarkerInvalidError, error.ErrorCode);
        }

        [Fact]
        public void Strict_marker_parser_rejects_unknown_properties()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            store.CreateMarker(CreateMarker());
            var path = MarkerPath(directories);
            var json = File.ReadAllText(path);
            File.WriteAllText(path, json.Insert(1, "\"connectionString\":\"Data Source=outside\","));

            var error = Assert.Throws<RestoreStateException>(() => store.ReadMarker());

            Assert.Equal(JsonPendingRestoreStore.MarkerInvalidError, error.ErrorCode);
        }

        [Fact]
        public void Strict_marker_parser_rejects_damaged_json()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            Directory.CreateDirectory(StateDirectory(directories));
            File.WriteAllText(MarkerPath(directories), "{damaged");

            var error = Assert.Throws<RestoreStateException>(() => store.ReadMarker());

            Assert.Equal(JsonPendingRestoreStore.MarkerInvalidError, error.ErrorCode);
        }

        [Fact]
        public void Receipt_only_allows_prepared_to_terminal_progress_for_the_same_restore()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            var marker = CreateMarker();
            var prepared = RestoreResultReceipt.FromMarker(marker, RestoreExecutionStage.Prepared);
            store.WriteReceipt(prepared);

            store.WriteReceipt(RestoreResultReceipt.FromMarker(marker, RestoreExecutionStage.Applied));

            Assert.Equal(RestoreExecutionStage.Applied, store.ReadReceipt()!.Stage);
            var different = RestoreResultReceipt.FromMarker(
                CreateMarker(Guid.NewGuid()),
                RestoreExecutionStage.Applied);
            var error = Assert.Throws<RestoreStateException>(() => store.WriteReceipt(different));
            Assert.Equal(JsonPendingRestoreStore.ReceiptConflictError, error.ErrorCode);
        }

        [Fact]
        public void Strict_receipt_parser_rejects_damaged_json()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            Directory.CreateDirectory(StateDirectory(directories));
            File.WriteAllText(ReceiptPath(directories), "[]");

            var error = Assert.Throws<RestoreStateException>(() => store.ReadReceipt());

            Assert.Equal(JsonPendingRestoreStore.ReceiptInvalidError, error.ErrorCode);
        }

        internal static PendingRestoreMarker CreateMarker(Guid? jobId = null) => new PendingRestoreMarker(
            PendingRestoreMarker.CurrentVersion,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            BackupKind.PanelDatabase,
            "primary",
            "panel-backup.zip",
            new string('a', 64),
            new RestoreJobSnapshot(
                jobId ?? Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                JobKind.Restore,
                JobStatus.PendingRestart,
                "owner",
                "restore-once",
                "correlation-1",
                new DateTimeOffset(2026, 7, 27, 1, 2, 3, TimeSpan.Zero)),
            RestoreExecutionStage.Prepared);

        private static string StateDirectory(TestDirectories directories) =>
            Path.Combine(directories.Panel, JsonPendingRestoreStore.StateDirectoryName);

        private static string MarkerPath(TestDirectories directories) =>
            Path.Combine(StateDirectory(directories), JsonPendingRestoreStore.MarkerFileName);

        private static string ReceiptPath(TestDirectories directories) =>
            Path.Combine(StateDirectory(directories), JsonPendingRestoreStore.ReceiptFileName);
    }
}
