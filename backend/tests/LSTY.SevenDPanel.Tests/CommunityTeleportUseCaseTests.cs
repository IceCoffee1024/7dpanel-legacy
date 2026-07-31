using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Community;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommunityTeleportUseCaseTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 2, 3, 4, TimeSpan.Zero);

        [Fact]
        public void Charged_teleport_reservation_and_reserved_intent_commit_together_and_replay_idempotently()
        {
            using var database = new TemporaryDatabase();
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            var store = new SqliteChargedTeleportOperationStore(database.Factory);
            var reservation = Reservation("operation-atomic");
            var intent = Intent("operation-atomic");

            var created = store.ReserveAndCreate(reservation, intent);
            var replayed = store.ReserveAndCreate(reservation, intent);
            var createdOperation = Assert.IsType<TeleportOperation>(created.Operation);
            var replayedOperation = Assert.IsType<TeleportOperation>(replayed.Operation);

            Assert.Equal(FundsReservationStatus.Reserved, created.Reservation.Status);
            Assert.Equal(TeleportOperationState.Reserved, createdOperation.State);
            Assert.Equal(created.Reservation.ReservationId, replayed.Reservation.ReservationId);
            Assert.Equal(createdOperation.OperationId, replayedOperation.OperationId);
            Assert.Equal(25, PlayerAccount(ledger).ReservedDebit);
            using var connection = database.Factory.Open();
            Assert.Equal(1L, Count(connection, "economy_reservations", reservation.ReservationId));
            Assert.Equal(1L, Count(connection, "teleport_operations", intent.OperationId));
        }

        [Fact]
        public void Charged_teleport_intent_write_failure_rolls_back_the_reservation()
        {
            using var database = new TemporaryDatabase();
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            var store = new SqliteChargedTeleportOperationStore(database.Factory);
            var reservation = Reservation("operation-rollback");
            var intent = Intent("operation-rollback");
            using (var connection = database.Factory.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TRIGGER reject_teleport_intent
                    BEFORE INSERT ON teleport_operations
                    BEGIN
                        SELECT RAISE(ABORT, 'intent write interrupted');
                    END;";
                command.ExecuteNonQuery();
            }

            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(
                (Action)(() => store.ReserveAndCreate(reservation, intent)));

            Assert.Equal(0, PlayerAccount(ledger).ReservedDebit);
            using var verify = database.Factory.Open();
            Assert.Equal(0L, Count(verify, "economy_reservations", reservation.ReservationId));
            Assert.Equal(0L, Count(verify, "teleport_operations", intent.OperationId));
        }

        [Fact]
        public void Homes_cities_and_friendships_enforce_fixed_rules()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteCommunityStore(database.Factory);

            store.SaveTeleportSettings(Settings(TeleportKind.Home, maxHomes: 1));
            var first = store.SaveHome(
                new PlayerHome("home-1", "EOS-A", "Base", Position(10, 70, 20), Now, Now, 0),
                1,
                null);

            Assert.Equal("Base", first.Name);
            Assert.Throws<CommunityLimitExceededException>(() => store.SaveHome(
                new PlayerHome("home-2", "EOS-A", "Mine", Position(20, 70, 20), Now, Now, 0),
                1,
                null));
            Assert.Throws<CommunityConflictException>(() => store.SaveHome(
                new PlayerHome("home-3", "EOS-A", "base", Position(30, 70, 20), Now, Now, 0),
                2,
                null));

            store.SaveCity(new City(
                "city-1", "Trader", "Public trader", false, Position(40, 70, 20), 0, Now, Now, 0));
            Assert.Null(store.FindEnabledCity("Trader"));

            var request = store.CreateFriendRequest(new FriendRequest(
                "request-1", "EOS-A", "EOS-B", FriendRequestState.Pending,
                null, Now, Now.AddMinutes(5), null, 0));
            var accepted = store.RespondToFriendRequest(
                request.RequestId, "EOS-B", true, "friendship-1", Now.AddMinutes(1));

            Assert.Equal(FriendRequestState.Accepted, accepted.State);
            Assert.True(store.AreFriends("EOS-A", "EOS-B"));
            Assert.True(store.RemoveFriendship("EOS-B", "EOS-A"));
            Assert.False(store.AreFriends("EOS-A", "EOS-B"));
        }

        [Fact]
        public void Home_use_case_carries_the_current_version_when_overwriting_a_home()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteCommunityStore(database.Factory);
            store.SaveTeleportSettings(Settings(TeleportKind.Home, maxHomes: 2));
            var homes = new HomeUseCases(store, () => Now);

            var created = homes.Save("home-1", "Base", Player(position: Position(10, 70, 20)));
            var updated = homes.Save("home-1", "Base", Player(position: Position(30, 70, 40)));

            Assert.Equal(0, created.RowVersion);
            Assert.Equal(1, updated.RowVersion);
            Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
            Assert.Equal(30, updated.Position.X);
            Assert.Equal(40, updated.Position.Z);
        }

        [Fact]
        public void Home_overwrite_rejects_a_stale_version_without_losing_the_first_update()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteCommunityStore(database.Factory);
            var created = store.SaveHome(
                new PlayerHome("home-1", "EOS-A", "Base", Position(10, 70, 20), Now, Now, 0),
                2,
                null);
            var firstWriter = new PlayerHome(
                created.HomeId, created.CrossplatformId, created.Name, Position(30, 70, 40),
                created.CreatedAtUtc, Now.AddSeconds(1), created.RowVersion);
            var staleWriter = new PlayerHome(
                created.HomeId, created.CrossplatformId, created.Name, Position(50, 70, 60),
                created.CreatedAtUtc, Now.AddSeconds(2), created.RowVersion);

            var updated = store.SaveHome(firstWriter, 2, created.RowVersion);
            var conflict = Assert.Throws<CommunityConflictException>(() =>
                store.SaveHome(staleWriter, 2, created.RowVersion));

            Assert.Equal("community_conflict", conflict.Code);
            Assert.Equal(1, updated.RowVersion);
            var persisted = Assert.IsType<PlayerHome>(store.FindHome("EOS-A", "Base"));
            Assert.Equal(updated.RowVersion, persisted.RowVersion);
            Assert.Equal(30, persisted.Position.X);
            Assert.Equal(40, persisted.Position.Z);
        }

        [Fact]
        public void Home_delete_allows_a_new_incarnation_but_rejects_a_stale_writer()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteCommunityStore(database.Factory);
            var created = store.SaveHome(
                new PlayerHome("home-1", "EOS-A", "Base", Position(10, 70, 20), Now, Now, 0),
                2,
                null);
            var staleWriter = new PlayerHome(
                created.HomeId, created.CrossplatformId, created.Name, Position(30, 70, 40),
                created.CreatedAtUtc, Now.AddSeconds(2), created.RowVersion);

            Assert.True(store.DeleteHome("EOS-A", "Base"));
            var recreated = store.SaveHome(
                new PlayerHome(
                    "home-2",
                    created.CrossplatformId,
                    created.Name,
                    Position(50, 70, 60),
                    Now.AddSeconds(1),
                    Now.AddSeconds(1),
                    0),
                2,
                null);
            var conflict = Assert.Throws<CommunityConflictException>(() =>
                store.SaveHome(staleWriter, 2, created.RowVersion));

            Assert.Equal("community_conflict", conflict.Code);
            Assert.Equal(0, recreated.RowVersion);
            Assert.NotEqual(created.HomeId, recreated.HomeId);
            Assert.Equal(Now.AddSeconds(1), recreated.CreatedAtUtc);
            var persisted = Assert.IsType<PlayerHome>(store.FindHome("EOS-A", "Base"));
            Assert.Equal(50, persisted.Position.X);
            Assert.Equal(60, persisted.Position.Z);
        }

        [Fact]
        public void Home_delete_rejects_a_stale_writer_instead_of_resurrecting_it()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteCommunityStore(database.Factory);
            var created = store.SaveHome(
                new PlayerHome("home-1", "EOS-A", "Base", Position(10, 70, 20), Now, Now, 0),
                2,
                null);
            var staleWriter = new PlayerHome(
                created.HomeId, created.CrossplatformId, created.Name, Position(30, 70, 40),
                created.CreatedAtUtc, Now.AddSeconds(1), created.RowVersion);

            Assert.True(store.DeleteHome("EOS-A", "Base"));
            var conflict = Assert.Throws<CommunityConflictException>(() =>
                store.SaveHome(staleWriter, 2, created.RowVersion));

            Assert.Equal("community_conflict", conflict.Code);
            Assert.Null(store.FindHome("EOS-A", "Base"));
        }

        [Fact]
        public void City_upsert_rejects_a_stale_version_without_losing_the_first_update()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteCommunityStore(database.Factory);
            var created = store.SaveCity(new City(
                "city-1", "Trader", "Initial", true, Position(10, 70, 20), 0, Now, Now, 0));
            var firstWriter = new City(
                created.CityId, created.Name, "First writer", created.Enabled,
                Position(30, 70, 40), created.SortOrder, created.CreatedAtUtc,
                Now.AddSeconds(1), created.RowVersion);
            var staleWriter = new City(
                created.CityId, created.Name, "Stale writer", created.Enabled,
                Position(50, 70, 60), created.SortOrder, created.CreatedAtUtc,
                Now.AddSeconds(2), created.RowVersion);

            var updated = store.SaveCity(firstWriter);
            var conflict = Assert.Throws<CommunityConflictException>(() => store.SaveCity(staleWriter));

            Assert.Equal("community_conflict", conflict.Code);
            Assert.Equal(1, updated.RowVersion);
            var persisted = store.ListCities().Single(city => city.CityId == created.CityId);
            Assert.Equal(updated.RowVersion, persisted.RowVersion);
            Assert.Equal("First writer", persisted.Description);
            Assert.Equal(30, persisted.Position.X);
            Assert.Equal(40, persisted.Position.Z);
        }

        [Fact]
        public async Task Confirmed_teleport_captures_fee_and_atomically_updates_cooldowns_and_return_point()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            community.SaveTeleportSettings(Settings(
                TeleportKind.Home,
                fee: 25,
                cooldown: TimeSpan.FromMinutes(2),
                globalCooldown: TimeSpan.FromMinutes(1)));
            community.SaveHome(
                new PlayerHome("home-1", "EOS-A", "Base", Position(100, 70, 200), Now, Now, 0),
                3,
                null);
            var gateway = new RecordingGateway(TeleportActionResult.Succeeded(Position(1, 65, 2)));
            var useCases = new TeleportUseCases(
                community,
                ledger,
                gateway,
                () => Now,
                new SqliteChargedTeleportOperationStore(database.Factory));

            var operation = await useCases.TeleportHomeAsync(
                Request("operation-1", Player()), "Base", CancellationToken.None);

            Assert.Equal(TeleportOperationState.Completed, operation.State);
            Assert.Equal(75, PlayerAccount(ledger).PostedBalance);
            Assert.Equal(0, PlayerAccount(ledger).ReservedDebit);
            Assert.Equal(Position(1, 65, 2), community.GetReturnPoint("EOS-A")!.Position);
            Assert.Equal(Now.AddMinutes(2), community.GetCooldown("EOS-A", TeleportKind.Home));
            Assert.Equal(Now.AddMinutes(1), community.GetCooldown("EOS-A", TeleportKind.Global));
            Assert.Equal(100d, gateway.LastCommand!.Destination.X);

            var exception = await Assert.ThrowsAsync<TeleportRejectedException>(() =>
                useCases.TeleportHomeAsync(
                    Request("operation-2", Player()), "Base", CancellationToken.None));
            Assert.Equal(TeleportFailureCodes.CooldownActive, exception.Code);
        }

        [Fact]
        public async Task Pre_dispatch_failure_releases_fee_without_overwriting_return_point()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            community.SaveTeleportSettings(Settings(TeleportKind.City, fee: 30));
            community.SaveCity(new City(
                "city-1", "Trader", "", true, Position(100, 70, 200), 0, Now, Now, 0));
            var gateway = new RecordingGateway(
                TeleportActionResult.Rejected(TeleportFailureCodes.TargetChanged));
            var useCases = new TeleportUseCases(community, ledger, gateway, () => Now);

            var operation = await useCases.TeleportCityAsync(
                Request("operation-failed", Player()), "Trader", CancellationToken.None);

            Assert.Equal(TeleportOperationState.Failed, operation.State);
            Assert.Equal(100, PlayerAccount(ledger).PostedBalance);
            Assert.Equal(0, PlayerAccount(ledger).ReservedDebit);
            Assert.Null(community.GetReturnPoint("EOS-A"));
            Assert.Null(community.GetCooldown("EOS-A", TeleportKind.City));
        }

        [Fact]
        public async Task Unknown_delivery_holds_fee_without_expiry_and_never_updates_cooldown_or_return_point()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            community.SaveTeleportSettings(Settings(TeleportKind.Admin, fee: 40));
            var gateway = new RecordingGateway(TeleportActionResult.ResultUnknown());
            var useCases = new TeleportUseCases(community, ledger, gateway, () => Now);

            var operation = await useCases.TeleportAdminAsync(
                Request("operation-unknown", Player()),
                Position(100, 70, 200),
                CancellationToken.None);

            Assert.Equal(TeleportOperationState.PendingReconciliation, operation.State);
            Assert.Equal(100, PlayerAccount(ledger).PostedBalance);
            Assert.Equal(40, PlayerAccount(ledger).ReservedDebit);
            using (var connection = database.Factory.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT expires_at_utc IS NULL
                    FROM economy_reservations
                    WHERE reservation_id = 'teleport-reservation:operation-unknown';";
                Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
            }
            Assert.Null(community.GetReturnPoint("EOS-A"));
            Assert.Null(community.GetCooldown("EOS-A", TeleportKind.Admin));
        }

        [Fact]
        public async Task Friend_teleport_requires_friendship_online_consent_same_world_and_safe_player_state()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            community.SaveTeleportSettings(Settings(TeleportKind.Friend));
            var request = community.CreateFriendRequest(new FriendRequest(
                "request-1", "EOS-A", "EOS-B", FriendRequestState.Pending,
                null, Now.AddMinutes(-2), Now.AddMinutes(2), null, 0));
            community.RespondToFriendRequest(
                request.RequestId, "EOS-B", true, "friendship-1", Now.AddMinutes(-1));
            var gateway = new RecordingGateway(TeleportActionResult.Succeeded(Position(0, 65, 0)));
            var useCases = new TeleportUseCases(community, ledger, gateway, () => Now);

            var target = Player(
                crossplatformId: "EOS-B",
                entityId: 8,
                position: Position(300, 70, 400),
                allowsFriendTeleport: true);
            var operation = await useCases.TeleportFriendAsync(
                Request("operation-friend", Player()), target, CancellationToken.None);

            Assert.Equal(TeleportOperationState.Completed, operation.State);
            Assert.Equal("EOS-B", operation.TargetCrossplatformId);
            Assert.Equal(300d, gateway.LastCommand!.Destination.X);

            var rejectedTarget = Player(
                crossplatformId: "EOS-B",
                entityId: 8,
                position: new WorldPosition("world-2", 300, 70, 400, 0),
                allowsFriendTeleport: false);
            var exception = await Assert.ThrowsAsync<TeleportRejectedException>(() =>
                useCases.TeleportFriendAsync(
                    Request("operation-rejected", Player()), rejectedTarget, CancellationToken.None));
            Assert.Equal(TeleportFailureCodes.TargetNotAllowed, exception.Code);
        }

        [Theory]
        [InlineData(false, true, true, false, TeleportFailureCodes.PlayerNotOnline)]
        [InlineData(true, false, true, false, TeleportFailureCodes.PlayerDead)]
        [InlineData(true, true, false, false, TeleportFailureCodes.PlayerNotSpawned)]
        [InlineData(true, true, true, true, TeleportFailureCodes.BloodMoonDenied)]
        public async Task Application_preflight_rejects_unsafe_source_before_reserving(
            bool online,
            bool alive,
            bool spawned,
            bool bloodMoon,
            string expectedCode)
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            ledger.GetOrCreatePlayerAccount("EOS-A", "open:A", 100, Now.AddMinutes(-1));
            community.SaveTeleportSettings(Settings(TeleportKind.Admin, fee: 10));
            var useCases = new TeleportUseCases(
                community,
                ledger,
                new RecordingGateway(TeleportActionResult.Succeeded(Position(0, 65, 0))),
                () => Now);
            var player = Player(online: online, alive: alive, spawned: spawned, bloodMoon: bloodMoon);

            var exception = await Assert.ThrowsAsync<TeleportRejectedException>(() =>
                useCases.TeleportAdminAsync(
                    Request("unsafe-operation", player), Position(100, 70, 200), CancellationToken.None));

            Assert.Equal(expectedCode, exception.Code);
            Assert.Equal(0, PlayerAccount(ledger).ReservedDebit);
        }

        [Fact]
        public async Task Tpa_accept_is_atomic_and_dispatches_the_fixed_request_once()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            community.SaveTeleportSettings(Settings(TeleportKind.Friend));
            var friendshipRequest = community.CreateFriendRequest(new FriendRequest(
                "friend-request", "EOS-A", "EOS-B", FriendRequestState.Pending,
                null, Now.AddMinutes(-2), Now.AddMinutes(2), null, 0));
            community.RespondToFriendRequest(
                friendshipRequest.RequestId,
                "EOS-B",
                true,
                "friendship-tpa",
                Now.AddMinutes(-1));
            var gateway = new CountingGateway(TeleportActionResult.Succeeded(Position(1, 65, 2)));
            var snapshots = new FixedPlayerSnapshots(
                Snapshot("Alice", Player()),
                Snapshot("Bob", Player(
                    "EOS-B", 8, Position(300, 70, 400), allowsFriendTeleport: true)));
            var ids = new Queue<string>(new[] { "tpa-request-1", "tpa-operation-1" });
            var useCases = new TeleportFriendRequestUseCases(
                community,
                new TeleportUseCases(community, ledger, gateway, () => Now),
                snapshots,
                TimeSpan.FromMinutes(1),
                () => Now,
                () => ids.Dequeue());

            var requested = useCases.Request("EOS-A", "Bob");
            var responses = await Task.WhenAll(
                useCases.AcceptAsync("EOS-B", TestContext.Current.CancellationToken),
                useCases.AcceptAsync("EOS-B", TestContext.Current.CancellationToken));

            Assert.Equal(TeleportFriendRequestCreateStatus.Created, requested.Status);
            Assert.Single(responses, response =>
                response.Status == TeleportFriendRequestResponseStatus.Accepted);
            Assert.Equal(1, gateway.Calls);
            Assert.Equal("EOS-A", gateway.LastCommand!.CrossplatformId);
            Assert.Equal(7, gateway.LastCommand.ExpectedEntityId);
            Assert.Equal("world-1", gateway.LastCommand.ExpectedWorldId);
            var persisted = community.GetTeleportFriendRequest(requested.Request.RequestId);
            Assert.Equal(TeleportFriendRequestState.Accepted, persisted.State);
            Assert.Equal("tpa-operation-1", persisted.TeleportOperationId);
        }

        [Fact]
        public void Tpa_rejection_is_terminal_and_never_dispatches()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            community.SaveTeleportSettings(Settings(TeleportKind.Friend));
            var friendshipRequest = community.CreateFriendRequest(new FriendRequest(
                "friend-request-rejected", "EOS-A", "EOS-B", FriendRequestState.Pending,
                null, Now.AddMinutes(-2), Now.AddMinutes(2), null, 0));
            community.RespondToFriendRequest(
                friendshipRequest.RequestId,
                "EOS-B",
                true,
                "friendship-tpa-rejected",
                Now.AddMinutes(-1));
            var gateway = new CountingGateway(TeleportActionResult.Succeeded(Position(1, 65, 2)));
            var useCases = new TeleportFriendRequestUseCases(
                community,
                new TeleportUseCases(community, ledger, gateway, () => Now),
                new FixedPlayerSnapshots(
                    Snapshot("Alice", Player()),
                    Snapshot("Bob", Player(
                        "EOS-B", 8, Position(300, 70, 400), allowsFriendTeleport: true))),
                TimeSpan.FromMinutes(1),
                () => Now,
                () => "tpa-request-rejected");

            var requested = useCases.Request("EOS-A", "Bob");
            var rejected = useCases.Reject("EOS-B");

            Assert.Equal(TeleportFriendRequestCreateStatus.Created, requested.Status);
            Assert.Equal(TeleportFriendRequestResponseStatus.Rejected, rejected.Status);
            Assert.Equal(0, gateway.Calls);
            var persisted = community.GetTeleportFriendRequest(requested.Request.RequestId);
            Assert.Equal(TeleportFriendRequestState.Rejected, persisted.State);
            Assert.Null(persisted.TeleportOperationId);
        }

        [Fact]
        public async Task Tpa_expiry_is_terminal_and_never_dispatches()
        {
            using var database = new TemporaryDatabase();
            var community = new SqliteCommunityStore(database.Factory);
            var ledger = new SqliteEconomyLedgerStore(database.Factory);
            community.SaveTeleportSettings(Settings(TeleportKind.Friend));
            var friendshipRequest = community.CreateFriendRequest(new FriendRequest(
                "friend-request-expired", "EOS-A", "EOS-B", FriendRequestState.Pending,
                null, Now.AddMinutes(-2), Now.AddMinutes(2), null, 0));
            community.RespondToFriendRequest(
                friendshipRequest.RequestId,
                "EOS-B",
                true,
                "friendship-tpa-expired",
                Now.AddMinutes(-1));
            var gateway = new CountingGateway(TeleportActionResult.Succeeded(Position(1, 65, 2)));
            var snapshots = new FixedPlayerSnapshots(
                Snapshot("Alice", Player()),
                Snapshot("Bob", Player(
                    "EOS-B", 8, Position(300, 70, 400), allowsFriendTeleport: true)));
            var requester = new TeleportFriendRequestUseCases(
                community,
                new TeleportUseCases(community, ledger, gateway, () => Now),
                snapshots,
                TimeSpan.FromMinutes(1),
                () => Now,
                () => "tpa-request-expired");
            var requested = requester.Request("EOS-A", "Bob");
            var responder = new TeleportFriendRequestUseCases(
                community,
                new TeleportUseCases(community, ledger, gateway, () => Now.AddMinutes(2)),
                snapshots,
                TimeSpan.FromMinutes(1),
                () => Now.AddMinutes(2),
                () => throw new InvalidOperationException("An expired request must not allocate an operation."));

            var expired = await responder.AcceptAsync("EOS-B", TestContext.Current.CancellationToken);

            Assert.Equal(TeleportFriendRequestResponseStatus.Expired, expired.Status);
            Assert.Equal(0, gateway.Calls);
            var persisted = community.GetTeleportFriendRequest(requested.Request.RequestId);
            Assert.Equal(TeleportFriendRequestState.Expired, persisted.State);
            Assert.Null(persisted.TeleportOperationId);
        }

        private static CommunityPlayerCommandSnapshot Snapshot(
            string name,
            TeleportPlayerSnapshot player) =>
            new CommunityPlayerCommandSnapshot(name, player, TimeSpan.FromMinutes(5));

        private static TeleportSettings Settings(
            TeleportKind kind,
            int? maxHomes = null,
            long fee = 0,
            TimeSpan? cooldown = null,
            TimeSpan? globalCooldown = null) => new TeleportSettings(
                kind,
                true,
                maxHomes,
                cooldown ?? TimeSpan.Zero,
                globalCooldown ?? TimeSpan.Zero,
                true,
                fee,
                Now,
                0);

        private static TeleportExecutionRequest Request(
            string operationId,
            TeleportPlayerSnapshot player) => new TeleportExecutionRequest(
                operationId,
                "idempotency:" + operationId,
                player,
                "Player",
                player.CrossplatformId,
                "correlation:" + operationId);

        private static TeleportPlayerSnapshot Player(
            string crossplatformId = "EOS-A",
            int entityId = 7,
            WorldPosition? position = null,
            bool online = true,
            bool alive = true,
            bool spawned = true,
            bool bloodMoon = false,
            bool allowsFriendTeleport = false) => new TeleportPlayerSnapshot(
                crossplatformId,
                entityId,
                position ?? Position(1, 65, 2),
                online,
                alive,
                spawned,
                bloodMoon,
                allowsFriendTeleport,
                new WorldBounds(-1000, 1000, -1000, 1000));

        private static WorldPosition Position(double x, double y, double z) =>
            new WorldPosition("world-1", x, y, z, 90);

        private static AccountSnapshot PlayerAccount(IEconomyLedgerStore ledger) =>
            ledger.QueryAccounts(new AccountKeysetQuery(10, includeSystem: false, search: "EOS-A"))
                .Accounts.Single(account => account.CrossplatformId == "EOS-A");

        private static FundsReservationDraft Reservation(string operationId) => new FundsReservationDraft(
            "teleport-reservation:" + operationId,
            "player:EOS-A",
            25,
            "idempotency:" + operationId + ":reserve",
            "Teleport",
            operationId,
            Now,
            null);

        private static TeleportOperationDraft Intent(string operationId) => new TeleportOperationDraft(
            operationId,
            TeleportKind.Admin,
            "EOS-A",
            null,
            7,
            "world-1",
            Position(100, 70, 200),
            "idempotency:" + operationId,
            "teleport-reservation:" + operationId,
            "Player",
            "EOS-A",
            "correlation:" + operationId,
            Now);

        private static long Count(Microsoft.Data.Sqlite.SqliteConnection connection, string table, string id)
        {
            using var command = connection.CreateCommand();
            command.CommandText = table == "economy_reservations"
                ? "SELECT COUNT(*) FROM economy_reservations WHERE reservation_id = $id;"
                : "SELECT COUNT(*) FROM teleport_operations WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", id);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private sealed class RecordingGateway : ICommunityGameGateway
        {
            private readonly TeleportActionResult result;

            public RecordingGateway(TeleportActionResult result) => this.result = result;

            public TeleportActionCommand? LastCommand { get; private set; }

            public Task<TeleportActionResult> TeleportAsync(
                TeleportActionCommand command,
                CancellationToken cancellationToken)
            {
                LastCommand = command;
                return Task.FromResult(result);
            }
        }

        private sealed class CountingGateway : ICommunityGameGateway
        {
            private readonly TeleportActionResult result;
            private int calls;

            public CountingGateway(TeleportActionResult result) => this.result = result;

            public int Calls => Volatile.Read(ref calls);
            public TeleportActionCommand? LastCommand { get; private set; }

            public Task<TeleportActionResult> TeleportAsync(
                TeleportActionCommand command,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                LastCommand = command;
                return Task.FromResult(result);
            }
        }

        private sealed class FixedPlayerSnapshots : ICommunityPlayerCommandSnapshotProvider
        {
            private readonly IReadOnlyList<CommunityPlayerCommandSnapshot> players;

            public FixedPlayerSnapshots(params CommunityPlayerCommandSnapshot[] players) =>
                this.players = players;

            public CommunityPlayerCommandSnapshot? FindOnlineByCrossplatformId(string crossplatformId) =>
                players.SingleOrDefault(player => string.Equals(
                    player.CrossplatformId,
                    crossplatformId,
                    StringComparison.Ordinal));

            public CommunityPlayerCommandSnapshot? ResolveOnline(string selector) =>
                players.SingleOrDefault(player =>
                    string.Equals(player.CrossplatformId, selector, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(player.DisplayName, selector, StringComparison.OrdinalIgnoreCase));

            public IReadOnlyList<CommunityPlayerCommandSnapshot> CaptureOnline() => players;
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryDatabase()
            {
                directory = Path.Combine(Path.GetTempPath(), "7dpanel-community-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                Factory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(Factory).Upgrade();
            }

            public SqliteConnectionFactory Factory { get; }

            public void Dispose()
            {
                Factory.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
