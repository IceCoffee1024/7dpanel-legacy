using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community
{
    public sealed class SqliteCommunityStore : ICommunityStore, ITeleportFriendRequestStore
    {
        private const string SettingsSelect = @"
            SELECT teleport_kind AS Kind, enabled AS Enabled, max_homes AS MaxHomes,
                   cooldown_ms AS CooldownMs, global_cooldown_ms AS GlobalCooldownMs,
                   deny_during_blood_moon AS DenyDuringBloodMoon, fee_amount AS FeeAmount,
                   updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM teleport_settings";

        private const string HomeSelect = @"
            SELECT home_id AS HomeId, crossplatform_id AS CrossplatformId, name AS Name,
                   world_id AS WorldId, x AS X, y AS Y, z AS Z, yaw AS Yaw,
                   created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
                   row_version AS RowVersion
            FROM player_homes";

        private const string CitySelect = @"
            SELECT city_id AS CityId, name AS Name, description AS Description,
                   enabled AS Enabled, world_id AS WorldId, x AS X, y AS Y, z AS Z,
                   yaw AS Yaw, sort_order AS SortOrder, created_at_utc AS CreatedAtUtc,
                   updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM cities";

        private const string FriendRequestSelect = @"
            SELECT request_id AS RequestId,
                   requester_crossplatform_id AS RequesterCrossplatformId,
                   target_crossplatform_id AS TargetCrossplatformId,
                   state AS State, friendship_id AS FriendshipId,
                   created_at_utc AS CreatedAtUtc, expires_at_utc AS ExpiresAtUtc,
                   responded_at_utc AS RespondedAtUtc, row_version AS RowVersion
            FROM friend_requests";

        private const string TeleportFriendRequestSelect = @"
            SELECT request_id AS RequestId, idempotency_key AS IdempotencyKey,
                   requester_crossplatform_id AS RequesterCrossplatformId,
                   requester_entity_id AS RequesterEntityId,
                   requester_world_id AS RequesterWorldId,
                   target_crossplatform_id AS TargetCrossplatformId,
                   target_entity_id AS TargetEntityId,
                   target_world_id AS TargetWorldId, state AS State,
                   teleport_operation_id AS TeleportOperationId,
                   created_at_utc AS CreatedAtUtc, expires_at_utc AS ExpiresAtUtc,
                   responded_at_utc AS RespondedAtUtc, row_version AS RowVersion
            FROM teleport_friend_requests";

        private const string OperationSelect = @"
            SELECT operation_id AS OperationId, teleport_kind AS Kind,
                   crossplatform_id AS CrossplatformId,
                   target_crossplatform_id AS TargetCrossplatformId,
                   expected_entity_id AS ExpectedEntityId,
                   expected_world_id AS ExpectedWorldId,
                   destination_world_id AS DestinationWorldId,
                   destination_x AS DestinationX, destination_y AS DestinationY,
                   destination_z AS DestinationZ, destination_yaw AS DestinationYaw,
                   origin_world_id AS OriginWorldId, origin_x AS OriginX,
                   origin_y AS OriginY, origin_z AS OriginZ, origin_yaw AS OriginYaw,
                   state AS State, idempotency_key AS IdempotencyKey,
                   reservation_id AS ReservationId, actor_kind AS ActorKind,
                   actor_id AS ActorId, correlation_id AS CorrelationId,
                   error_code AS ErrorCode, created_at_utc AS CreatedAtUtc,
                   updated_at_utc AS UpdatedAtUtc, completed_at_utc AS CompletedAtUtc,
                   row_version AS RowVersion
            FROM teleport_operations";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteCommunityStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public TeleportSettings GetTeleportSettings(TeleportKind kind)
        {
            RequireOperationKind(kind, nameof(kind));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<SettingsRow>(
                SettingsSelect + " WHERE teleport_kind = @Kind;",
                new { Kind = kind.ToString() }) ?? throw new CommunityNotFoundException();
            return ToSettings(row);
        }

        public TeleportSettings SaveTeleportSettings(TeleportSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var parameters = new
            {
                Kind = settings.Kind.ToString(),
                Enabled = settings.Enabled ? 1 : 0,
                settings.MaxHomes,
                CooldownMs = ToMilliseconds(settings.Cooldown),
                GlobalCooldownMs = ToMilliseconds(settings.GlobalCooldown),
                DenyDuringBloodMoon = settings.DenyDuringBloodMoon ? 1 : 0,
                settings.FeeAmount,
                UpdatedAtUtc = settings.UpdatedAtUtc.ToUnixTimeMilliseconds(),
                ExpectedRowVersion = settings.RowVersion
            };
            var exists = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM teleport_settings WHERE teleport_kind = @Kind;",
                parameters,
                transaction) != 0;
            if (!exists)
            {
                if (settings.RowVersion != 0) throw new CommunityConflictException();
                connection.Execute(
                    @"INSERT INTO teleport_settings (
                          teleport_kind, enabled, max_homes, cooldown_ms,
                          global_cooldown_ms, deny_during_blood_moon, fee_amount,
                          updated_at_utc, row_version)
                      VALUES (@Kind, @Enabled, @MaxHomes, @CooldownMs,
                          @GlobalCooldownMs, @DenyDuringBloodMoon, @FeeAmount,
                          @UpdatedAtUtc, 0);",
                    parameters,
                    transaction);
            }
            else if (connection.Execute(
                    @"UPDATE teleport_settings
                      SET enabled = @Enabled, max_homes = @MaxHomes,
                          cooldown_ms = @CooldownMs,
                          global_cooldown_ms = @GlobalCooldownMs,
                          deny_during_blood_moon = @DenyDuringBloodMoon,
                          fee_amount = @FeeAmount,
                          updated_at_utc = @UpdatedAtUtc,
                          row_version = row_version + 1
                      WHERE teleport_kind = @Kind
                        AND row_version = @ExpectedRowVersion;",
                    parameters,
                    transaction) != 1)
            {
                throw new CommunityConflictException();
            }
            var stored = connection.QuerySingle<SettingsRow>(
                SettingsSelect + " WHERE teleport_kind = @Kind;",
                new { Kind = settings.Kind.ToString() },
                transaction);
            transaction.Commit();
            return ToSettings(stored);
        }

        public PlayerHome SaveHome(PlayerHome home, int maxHomes)
        {
            if (home == null) throw new ArgumentNullException(nameof(home));
            if (maxHomes < 0) throw new ArgumentOutOfRangeException(nameof(maxHomes));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existingById = connection.QuerySingleOrDefault<HomeRow>(
                HomeSelect + " WHERE home_id = @HomeId;",
                new { home.HomeId },
                transaction);
            if (existingById != null &&
                !string.Equals(existingById.CrossplatformId, home.CrossplatformId, StringComparison.Ordinal))
            {
                throw new CommunityConflictException();
            }
            var nameOwner = connection.QuerySingleOrDefault<HomeRow>(
                HomeSelect + " WHERE crossplatform_id = @CrossplatformId AND name = @Name;",
                new { home.CrossplatformId, home.Name },
                transaction);
            if (nameOwner != null && !string.Equals(nameOwner.HomeId, home.HomeId, StringComparison.Ordinal))
                throw new CommunityConflictException();
            var count = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM player_homes WHERE crossplatform_id = @CrossplatformId AND home_id <> @HomeId;",
                new { home.CrossplatformId, home.HomeId },
                transaction);
            if (count >= maxHomes) throw new CommunityLimitExceededException();

            if (existingById == null)
            {
                connection.Execute(
                    @"INSERT INTO player_homes (
                          home_id, crossplatform_id, name, world_id, x, y, z, yaw,
                          created_at_utc, updated_at_utc, row_version)
                      VALUES (@HomeId, @CrossplatformId, @Name, @WorldId, @X, @Y, @Z, @Yaw,
                          @CreatedAtUtc, @UpdatedAtUtc, 0);",
                    HomeParameters(home),
                    transaction);
            }
            else
            {
                connection.Execute(
                    @"UPDATE player_homes
                      SET name = @Name, world_id = @WorldId, x = @X, y = @Y, z = @Z,
                          yaw = @Yaw, updated_at_utc = @UpdatedAtUtc,
                          row_version = row_version + 1
                      WHERE home_id = @HomeId;",
                    HomeParameters(home),
                    transaction);
            }

            var stored = connection.QuerySingle<HomeRow>(
                HomeSelect + " WHERE home_id = @HomeId;",
                new { home.HomeId },
                transaction);
            transaction.Commit();
            return ToHome(stored);
        }

        public IReadOnlyList<PlayerHome> ListHomes(string crossplatformId)
        {
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            return connection.Query<HomeRow>(
                    HomeSelect + " WHERE crossplatform_id = @CrossplatformId ORDER BY name ASC, home_id ASC;",
                    new { CrossplatformId = crossplatformId })
                .Select(ToHome)
                .ToArray();
        }

        public PlayerHome? FindHome(string crossplatformId, string name)
        {
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            name = RequireText(name, nameof(name));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<HomeRow>(
                HomeSelect + " WHERE crossplatform_id = @CrossplatformId AND name = @Name;",
                new { CrossplatformId = crossplatformId, Name = name });
            return row == null ? null : ToHome(row);
        }

        public bool DeleteHome(string crossplatformId, string name)
        {
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            name = RequireText(name, nameof(name));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                "DELETE FROM player_homes WHERE crossplatform_id = @CrossplatformId AND name = @Name;",
                new { CrossplatformId = crossplatformId, Name = name }) == 1;
        }

        public City SaveCity(City city)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var nameOwner = connection.QuerySingleOrDefault<CityRow>(
                CitySelect + " WHERE name = @Name;",
                new { city.Name },
                transaction);
            if (nameOwner != null && !string.Equals(nameOwner.CityId, city.CityId, StringComparison.Ordinal))
                throw new CommunityConflictException();
            connection.Execute(
                @"INSERT INTO cities (
                      city_id, name, description, enabled, world_id, x, y, z, yaw,
                      sort_order, created_at_utc, updated_at_utc, row_version)
                  VALUES (@CityId, @Name, @Description, @Enabled, @WorldId, @X, @Y, @Z, @Yaw,
                      @SortOrder, @CreatedAtUtc, @UpdatedAtUtc, 0)
                  ON CONFLICT(city_id) DO UPDATE SET
                      name = excluded.name, description = excluded.description,
                      enabled = excluded.enabled, world_id = excluded.world_id,
                      x = excluded.x, y = excluded.y, z = excluded.z, yaw = excluded.yaw,
                      sort_order = excluded.sort_order, updated_at_utc = excluded.updated_at_utc,
                      row_version = cities.row_version + 1;",
                new
                {
                    city.CityId,
                    city.Name,
                    city.Description,
                    Enabled = city.Enabled ? 1 : 0,
                    WorldId = city.Position.WorldId,
                    city.Position.X,
                    city.Position.Y,
                    city.Position.Z,
                    city.Position.Yaw,
                    city.SortOrder,
                    CreatedAtUtc = city.CreatedAtUtc.ToUnixTimeMilliseconds(),
                    UpdatedAtUtc = city.UpdatedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            var stored = connection.QuerySingle<CityRow>(
                CitySelect + " WHERE city_id = @CityId;",
                new { city.CityId },
                transaction);
            transaction.Commit();
            return ToCity(stored);
        }

        public IReadOnlyList<City> ListEnabledCities()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<CityRow>(
                    CitySelect + " WHERE enabled = 1 ORDER BY sort_order ASC, city_id ASC;")
                .Select(ToCity)
                .ToArray();
        }

        public IReadOnlyList<City> ListCities()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<CityRow>(
                    CitySelect + " ORDER BY sort_order ASC, city_id ASC;")
                .Select(ToCity)
                .ToArray();
        }

        public City? FindEnabledCity(string name)
        {
            name = RequireText(name, nameof(name));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<CityRow>(
                CitySelect + " WHERE enabled = 1 AND name = @Name;",
                new { Name = name });
            return row == null ? null : ToCity(row);
        }

        public FriendRequest CreateFriendRequest(FriendRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.State != FriendRequestState.Pending)
                throw new ArgumentException("A new friend request must be pending.", nameof(request));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var pair = CanonicalPair(request.RequesterCrossplatformId, request.TargetCrossplatformId);
            if (FriendshipExists(connection, transaction, pair.First, pair.Second) ||
                connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM friend_requests
                      WHERE state = 'Pending' AND (
                          (requester_crossplatform_id = @First AND target_crossplatform_id = @Second)
                          OR (requester_crossplatform_id = @Second AND target_crossplatform_id = @First));",
                    new { pair.First, pair.Second },
                    transaction) != 0)
            {
                throw new CommunityConflictException();
            }
            connection.Execute(
                @"INSERT INTO friend_requests (
                      request_id, requester_crossplatform_id, target_crossplatform_id,
                      state, friendship_id, created_at_utc, expires_at_utc,
                      responded_at_utc, row_version)
                  VALUES (@RequestId, @RequesterCrossplatformId, @TargetCrossplatformId,
                      'Pending', NULL, @CreatedAtUtc, @ExpiresAtUtc, NULL, 0);",
                new
                {
                    request.RequestId,
                    request.RequesterCrossplatformId,
                    request.TargetCrossplatformId,
                    CreatedAtUtc = request.CreatedAtUtc.ToUnixTimeMilliseconds(),
                    ExpiresAtUtc = request.ExpiresAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            var stored = GetFriendRequest(connection, transaction, request.RequestId);
            transaction.Commit();
            return ToFriendRequest(stored);
        }

        public FriendRequest RespondToFriendRequest(
            string requestId,
            string responderCrossplatformId,
            bool accept,
            string? friendshipId,
            DateTimeOffset respondedAtUtc)
        {
            requestId = RequireText(requestId, nameof(requestId));
            responderCrossplatformId = RequireText(responderCrossplatformId, nameof(responderCrossplatformId));
            RequireUtc(respondedAtUtc, nameof(respondedAtUtc));
            if (accept) friendshipId = RequireText(friendshipId, nameof(friendshipId));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var request = GetFriendRequest(connection, transaction, requestId);
            if (!string.Equals(request.TargetCrossplatformId, responderCrossplatformId, StringComparison.Ordinal) ||
                !string.Equals(request.State, FriendRequestState.Pending.ToString(), StringComparison.Ordinal) ||
                request.ExpiresAtUtc <= respondedAtUtc.ToUnixTimeMilliseconds())
            {
                throw new CommunityConflictException();
            }

            if (accept)
            {
                var pair = CanonicalPair(request.RequesterCrossplatformId, request.TargetCrossplatformId);
                if (FriendshipExists(connection, transaction, pair.First, pair.Second))
                    throw new CommunityConflictException();
                connection.Execute(
                    @"INSERT INTO friendships (
                          friendship_id, member_a_crossplatform_id, member_b_crossplatform_id,
                          created_by_crossplatform_id, accepted_at_utc)
                      VALUES (@FriendshipId, @First, @Second, @CreatedBy, @AcceptedAtUtc);",
                    new
                    {
                        FriendshipId = friendshipId,
                        pair.First,
                        pair.Second,
                        CreatedBy = request.RequesterCrossplatformId,
                        AcceptedAtUtc = respondedAtUtc.ToUnixTimeMilliseconds()
                    },
                    transaction);
            }

            var changed = connection.Execute(
                @"UPDATE friend_requests
                  SET state = @State, friendship_id = @FriendshipId,
                      responded_at_utc = @RespondedAtUtc, row_version = row_version + 1
                  WHERE request_id = @RequestId AND state = 'Pending' AND row_version = @RowVersion;",
                new
                {
                    State = accept ? FriendRequestState.Accepted.ToString() : FriendRequestState.Rejected.ToString(),
                    FriendshipId = accept ? friendshipId : null,
                    RespondedAtUtc = respondedAtUtc.ToUnixTimeMilliseconds(),
                    RequestId = requestId,
                    request.RowVersion
                },
                transaction);
            if (changed != 1) throw new CommunityConflictException();
            var stored = GetFriendRequest(connection, transaction, requestId);
            transaction.Commit();
            return ToFriendRequest(stored);
        }

        public bool AreFriends(string firstCrossplatformId, string secondCrossplatformId)
        {
            var pair = CanonicalPair(firstCrossplatformId, secondCrossplatformId);
            using var connection = connectionFactory.Open();
            return connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM friendships
                  WHERE member_a_crossplatform_id = @First AND member_b_crossplatform_id = @Second;",
                new { pair.First, pair.Second }) != 0;
        }

        public IReadOnlyList<Friendship> ListFriendships()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<FriendshipRow>(
                    @"SELECT friendship_id AS FriendshipId,
                             member_a_crossplatform_id AS MemberACrossplatformId,
                             member_b_crossplatform_id AS MemberBCrossplatformId,
                             created_by_crossplatform_id AS CreatedByCrossplatformId,
                             accepted_at_utc AS AcceptedAtUtc
                      FROM friendships
                      ORDER BY accepted_at_utc ASC, friendship_id ASC;")
                .Select(ToFriendship)
                .ToArray();
        }

        public bool RemoveFriendship(string firstCrossplatformId, string secondCrossplatformId)
        {
            var pair = CanonicalPair(firstCrossplatformId, secondCrossplatformId);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var friendshipId = connection.QuerySingleOrDefault<string>(
                @"SELECT friendship_id FROM friendships
                  WHERE member_a_crossplatform_id = @First AND member_b_crossplatform_id = @Second;",
                new { pair.First, pair.Second },
                transaction);
            if (friendshipId == null)
            {
                transaction.Commit();
                return false;
            }
            connection.Execute(
                @"UPDATE friend_requests
                  SET friendship_id = NULL, row_version = row_version + 1
                  WHERE friendship_id = @FriendshipId;",
                new { FriendshipId = friendshipId },
                transaction);
            var removed = connection.Execute(
                @"DELETE FROM friendships
                  WHERE member_a_crossplatform_id = @First AND member_b_crossplatform_id = @Second;",
                new { pair.First, pair.Second },
                transaction) == 1;
            transaction.Commit();
            return removed;
        }

        public TeleportFriendRequest CreateTeleportFriendRequest(TeleportFriendRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.State != TeleportFriendRequestState.Pending)
                throw new ArgumentException("A new teleport friend request must be pending.", nameof(request));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<TeleportFriendRequestRow>(
                TeleportFriendRequestSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { request.IdempotencyKey },
                transaction);
            if (existing != null)
            {
                EnsureTeleportFriendRequestMatches(existing, request);
                transaction.Commit();
                return ToTeleportFriendRequest(existing);
            }
            if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM teleport_friend_requests WHERE request_id = @RequestId;",
                    new { request.RequestId },
                    transaction) != 0 ||
                connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM teleport_friend_requests
                      WHERE target_crossplatform_id = @TargetCrossplatformId AND state = 'Pending';",
                    new { request.TargetCrossplatformId },
                    transaction) != 0)
            {
                throw new CommunityConflictException();
            }

            connection.Execute(
                @"INSERT INTO teleport_friend_requests (
                      request_id, idempotency_key,
                      requester_crossplatform_id, requester_entity_id, requester_world_id,
                      target_crossplatform_id, target_entity_id, target_world_id,
                      state, teleport_operation_id, created_at_utc, expires_at_utc,
                      responded_at_utc, row_version)
                  VALUES (@RequestId, @IdempotencyKey,
                      @RequesterCrossplatformId, @RequesterEntityId, @RequesterWorldId,
                      @TargetCrossplatformId, @TargetEntityId, @TargetWorldId,
                      'Pending', NULL, @CreatedAtUtc, @ExpiresAtUtc, NULL, 0);",
                new
                {
                    request.RequestId,
                    request.IdempotencyKey,
                    request.RequesterCrossplatformId,
                    request.RequesterEntityId,
                    request.RequesterWorldId,
                    request.TargetCrossplatformId,
                    request.TargetEntityId,
                    request.TargetWorldId,
                    CreatedAtUtc = request.CreatedAtUtc.ToUnixTimeMilliseconds(),
                    ExpiresAtUtc = request.ExpiresAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            var stored = GetTeleportFriendRequest(connection, transaction, request.RequestId);
            transaction.Commit();
            return ToTeleportFriendRequest(stored);
        }

        public TeleportFriendRequest? GetTeleportFriendRequest(string requestId)
        {
            requestId = RequireText(requestId, nameof(requestId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<TeleportFriendRequestRow>(
                TeleportFriendRequestSelect + " WHERE request_id = @RequestId;",
                new { RequestId = requestId });
            return row == null ? null : ToTeleportFriendRequest(row);
        }

        public TeleportFriendRequest? FindPendingTeleportFriendRequest(string targetCrossplatformId)
        {
            targetCrossplatformId = RequireText(targetCrossplatformId, nameof(targetCrossplatformId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<TeleportFriendRequestRow>(
                TeleportFriendRequestSelect + @"
                    WHERE target_crossplatform_id = @TargetCrossplatformId AND state = 'Pending'
                    ORDER BY created_at_utc ASC, request_id ASC LIMIT 1;",
                new { TargetCrossplatformId = targetCrossplatformId });
            return row == null ? null : ToTeleportFriendRequest(row);
        }

        public bool TryRespondToTeleportFriendRequest(
            string requestId,
            string responderCrossplatformId,
            bool accept,
            string? teleportOperationId,
            DateTimeOffset respondedAtUtc)
        {
            requestId = RequireText(requestId, nameof(requestId));
            responderCrossplatformId = RequireText(responderCrossplatformId, nameof(responderCrossplatformId));
            RequireUtc(respondedAtUtc, nameof(respondedAtUtc));
            teleportOperationId = accept
                ? RequireText(teleportOperationId, nameof(teleportOperationId))
                : null;
            return ExecuteTeleportFriendRequestTransition(
                requestId,
                responderCrossplatformId,
                accept ? TeleportFriendRequestState.Accepted : TeleportFriendRequestState.Rejected,
                teleportOperationId,
                respondedAtUtc,
                "expires_at_utc > @RespondedAtUtc");
        }

        public bool TryExpireTeleportFriendRequest(
            string requestId,
            string responderCrossplatformId,
            DateTimeOffset expiredAtUtc)
        {
            requestId = RequireText(requestId, nameof(requestId));
            responderCrossplatformId = RequireText(responderCrossplatformId, nameof(responderCrossplatformId));
            RequireUtc(expiredAtUtc, nameof(expiredAtUtc));
            return ExecuteTeleportFriendRequestTransition(
                requestId,
                responderCrossplatformId,
                TeleportFriendRequestState.Expired,
                null,
                expiredAtUtc,
                "expires_at_utc <= @RespondedAtUtc");
        }

        public PlayerReturnPoint? GetReturnPoint(string crossplatformId)
        {
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ReturnPointRow>(
                @"SELECT crossplatform_id AS CrossplatformId,
                         source_operation_id AS SourceOperationId, world_id AS WorldId,
                         x AS X, y AS Y, z AS Z, yaw AS Yaw,
                         saved_at_utc AS SavedAtUtc, row_version AS RowVersion
                  FROM player_return_points WHERE crossplatform_id = @CrossplatformId;",
                new { CrossplatformId = crossplatformId });
            return row == null ? null : ToReturnPoint(row);
        }

        public DateTimeOffset? GetCooldown(string crossplatformId, TeleportKind kind)
        {
            crossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            RequireDefined(kind, nameof(kind));
            using var connection = connectionFactory.Open();
            var value = connection.QuerySingleOrDefault<long?>(
                @"SELECT available_at_utc FROM teleport_cooldowns
                  WHERE crossplatform_id = @CrossplatformId AND cooldown_kind = @Kind;",
                new { CrossplatformId = crossplatformId, Kind = kind.ToString() });
            return value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null;
        }

        public TeleportOperation CreateTeleportOperation(TeleportOperationDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var operation = CreateTeleportOperation(connection, transaction, draft);
            transaction.Commit();
            return operation;
        }

        internal static TeleportOperation CreateTeleportOperation(
            SqliteConnection connection,
            SqliteTransaction transaction,
            TeleportOperationDraft draft)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var existing = connection.QuerySingleOrDefault<OperationRow>(
                OperationSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { draft.IdempotencyKey },
                transaction);
            if (existing != null)
            {
                EnsureOperationMatches(existing, draft);
                return ToOperation(existing);
            }
            if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM teleport_operations WHERE operation_id = @OperationId;",
                    new { draft.OperationId },
                    transaction) != 0)
            {
                throw new CommunityConflictException();
            }

            var active = connection.QuerySingleOrDefault<CooldownRow>(
                @"SELECT cooldown_kind AS Kind, available_at_utc AS AvailableAtUtc
                  FROM teleport_cooldowns
                  WHERE crossplatform_id = @CrossplatformId
                    AND cooldown_kind IN ('Global', @Kind)
                    AND available_at_utc > @CreatedAtUtc
                  ORDER BY available_at_utc DESC LIMIT 1;",
                new
                {
                    draft.CrossplatformId,
                    Kind = draft.Kind.ToString(),
                    CreatedAtUtc = draft.CreatedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            if (active != null)
            {
                throw new TeleportRejectedException(
                    TeleportFailureCodes.CooldownActive,
                    DateTimeOffset.FromUnixTimeMilliseconds(active.AvailableAtUtc));
            }
            if (connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM teleport_operations
                      WHERE crossplatform_id = @CrossplatformId
                        AND state IN ('Reserved', 'Dispatching', 'PendingReconciliation');",
                    new { draft.CrossplatformId },
                    transaction) != 0)
            {
                throw new TeleportRejectedException(TeleportFailureCodes.TeleportAlreadyInProgress);
            }

            connection.Execute(
                @"INSERT INTO teleport_operations (
                      operation_id, teleport_kind, crossplatform_id, target_crossplatform_id,
                      expected_entity_id, expected_world_id, destination_world_id,
                      destination_x, destination_y, destination_z, destination_yaw,
                      origin_world_id, origin_x, origin_y, origin_z, origin_yaw,
                      state, idempotency_key, reservation_id, actor_kind, actor_id,
                      correlation_id, error_code, created_at_utc, updated_at_utc,
                      completed_at_utc, row_version)
                  VALUES (@OperationId, @Kind, @CrossplatformId, @TargetCrossplatformId,
                      @ExpectedEntityId, @ExpectedWorldId, @DestinationWorldId,
                      @DestinationX, @DestinationY, @DestinationZ, @DestinationYaw,
                      NULL, NULL, NULL, NULL, NULL,
                      'Reserved', @IdempotencyKey, @ReservationId, @ActorKind, @ActorId,
                      @CorrelationId, NULL, @CreatedAtUtc, @CreatedAtUtc, NULL, 0);",
                OperationParameters(draft),
                transaction);
            var stored = GetOperation(connection, transaction, draft.OperationId);
            return ToOperation(stored);
        }

        public TeleportOperation? FindTeleportOperation(string operationId)
        {
            operationId = RequireText(operationId, nameof(operationId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<OperationRow>(
                OperationSelect + " WHERE operation_id = @OperationId;",
                new { OperationId = operationId });
            return row == null ? null : ToOperation(row);
        }

        public IReadOnlyList<TeleportOperation> ListTeleportOperations()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<OperationRow>(
                    OperationSelect + " ORDER BY created_at_utc ASC, operation_id ASC;")
                .Select(ToOperation)
                .ToArray();
        }

        public bool TryTransitionTeleportOperation(
            string operationId,
            TeleportOperationState expectedState,
            TeleportOperationState nextState,
            string? errorCode,
            DateTimeOffset updatedAtUtc)
        {
            operationId = RequireText(operationId, nameof(operationId));
            RequireDefined(expectedState, nameof(expectedState));
            RequireDefined(nextState, nameof(nextState));
            RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (!TeleportStateMachine.CanTransition(expectedState, nextState))
                throw new InvalidOperationException("The teleport state transition is not allowed.");
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE teleport_operations
                  SET state = @NextState, error_code = @ErrorCode,
                      updated_at_utc = @UpdatedAtUtc,
                      completed_at_utc = CASE
                          WHEN @NextState IN ('Completed', 'Failed', 'Refunded') THEN @UpdatedAtUtc
                          ELSE NULL END,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = @ExpectedState;",
                new
                {
                    OperationId = operationId,
                    ExpectedState = expectedState.ToString(),
                    NextState = nextState.ToString(),
                    ErrorCode = OptionalText(errorCode),
                    UpdatedAtUtc = updatedAtUtc.ToUnixTimeMilliseconds()
                }) == 1;
        }

        public TeleportOperation CompleteTeleportOperation(
            string operationId,
            WorldPosition origin,
            DateTimeOffset kindAvailableAtUtc,
            DateTimeOffset globalAvailableAtUtc,
            DateTimeOffset completedAtUtc)
        {
            operationId = RequireText(operationId, nameof(operationId));
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            RequireUtc(kindAvailableAtUtc, nameof(kindAvailableAtUtc));
            RequireUtc(globalAvailableAtUtc, nameof(globalAvailableAtUtc));
            RequireUtc(completedAtUtc, nameof(completedAtUtc));
            if (kindAvailableAtUtc < completedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(kindAvailableAtUtc));
            if (globalAvailableAtUtc < completedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(globalAvailableAtUtc));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var current = GetOperation(connection, transaction, operationId);
            if (string.Equals(current.State, TeleportOperationState.Completed.ToString(), StringComparison.Ordinal))
            {
                transaction.Commit();
                return ToOperation(current);
            }
            if (!string.Equals(current.State, TeleportOperationState.Dispatching.ToString(), StringComparison.Ordinal))
                throw new CommunityConflictException();
            if (!string.Equals(current.ExpectedWorldId, origin.WorldId, StringComparison.Ordinal))
                throw new CommunityConflictException();

            var changed = connection.Execute(
                @"UPDATE teleport_operations
                  SET origin_world_id = @OriginWorldId, origin_x = @OriginX,
                      origin_y = @OriginY, origin_z = @OriginZ, origin_yaw = @OriginYaw,
                      state = 'Completed', error_code = NULL,
                      updated_at_utc = @CompletedAtUtc, completed_at_utc = @CompletedAtUtc,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = 'Dispatching'
                    AND row_version = @RowVersion;",
                new
                {
                    OperationId = operationId,
                    OriginWorldId = origin.WorldId,
                    OriginX = origin.X,
                    OriginY = origin.Y,
                    OriginZ = origin.Z,
                    OriginYaw = origin.Yaw,
                    CompletedAtUtc = completedAtUtc.ToUnixTimeMilliseconds(),
                    current.RowVersion
                },
                transaction);
            if (changed != 1) throw new CommunityConflictException();

            UpsertCooldown(
                connection,
                transaction,
                current.CrossplatformId,
                current.Kind,
                operationId,
                kindAvailableAtUtc,
                completedAtUtc);
            UpsertCooldown(
                connection,
                transaction,
                current.CrossplatformId,
                TeleportKind.Global.ToString(),
                operationId,
                globalAvailableAtUtc,
                completedAtUtc);
            connection.Execute(
                @"INSERT INTO player_return_points (
                      crossplatform_id, source_operation_id, world_id, x, y, z, yaw,
                      saved_at_utc, row_version)
                  VALUES (@CrossplatformId, @OperationId, @WorldId, @X, @Y, @Z, @Yaw,
                      @SavedAtUtc, 0)
                  ON CONFLICT(crossplatform_id) DO UPDATE SET
                      source_operation_id = excluded.source_operation_id,
                      world_id = excluded.world_id, x = excluded.x, y = excluded.y,
                      z = excluded.z, yaw = excluded.yaw,
                      saved_at_utc = excluded.saved_at_utc,
                      row_version = player_return_points.row_version + 1;",
                new
                {
                    current.CrossplatformId,
                    OperationId = operationId,
                    WorldId = origin.WorldId,
                    origin.X,
                    origin.Y,
                    origin.Z,
                    origin.Yaw,
                    SavedAtUtc = completedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            var stored = GetOperation(connection, transaction, operationId);
            transaction.Commit();
            return ToOperation(stored);
        }

        private static void UpsertCooldown(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string crossplatformId,
            string kind,
            string operationId,
            DateTimeOffset availableAtUtc,
            DateTimeOffset updatedAtUtc)
        {
            connection.Execute(
                @"INSERT INTO teleport_cooldowns (
                      crossplatform_id, cooldown_kind, available_at_utc,
                      last_operation_id, updated_at_utc)
                  VALUES (@CrossplatformId, @Kind, @AvailableAtUtc,
                      @OperationId, @UpdatedAtUtc)
                  ON CONFLICT(crossplatform_id, cooldown_kind) DO UPDATE SET
                      available_at_utc = CASE
                          WHEN excluded.available_at_utc > teleport_cooldowns.available_at_utc
                              THEN excluded.available_at_utc
                          ELSE teleport_cooldowns.available_at_utc END,
                      last_operation_id = excluded.last_operation_id,
                      updated_at_utc = excluded.updated_at_utc;",
                new
                {
                    CrossplatformId = crossplatformId,
                    Kind = kind,
                    AvailableAtUtc = availableAtUtc.ToUnixTimeMilliseconds(),
                    OperationId = operationId,
                    UpdatedAtUtc = updatedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
        }

        private static object HomeParameters(PlayerHome home) => new
        {
            home.HomeId,
            home.CrossplatformId,
            home.Name,
            WorldId = home.Position.WorldId,
            home.Position.X,
            home.Position.Y,
            home.Position.Z,
            home.Position.Yaw,
            CreatedAtUtc = home.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtc = home.UpdatedAtUtc.ToUnixTimeMilliseconds()
        };

        private static object OperationParameters(TeleportOperationDraft draft) => new
        {
            draft.OperationId,
            Kind = draft.Kind.ToString(),
            draft.CrossplatformId,
            draft.TargetCrossplatformId,
            draft.ExpectedEntityId,
            draft.ExpectedWorldId,
            DestinationWorldId = draft.Destination.WorldId,
            DestinationX = draft.Destination.X,
            DestinationY = draft.Destination.Y,
            DestinationZ = draft.Destination.Z,
            DestinationYaw = draft.Destination.Yaw,
            draft.IdempotencyKey,
            draft.ReservationId,
            draft.ActorKind,
            draft.ActorId,
            draft.CorrelationId,
            CreatedAtUtc = draft.CreatedAtUtc.ToUnixTimeMilliseconds()
        };

        private static void EnsureOperationMatches(OperationRow row, TeleportOperationDraft draft)
        {
            if (!string.Equals(row.OperationId, draft.OperationId, StringComparison.Ordinal) ||
                !string.Equals(row.Kind, draft.Kind.ToString(), StringComparison.Ordinal) ||
                !string.Equals(row.CrossplatformId, draft.CrossplatformId, StringComparison.Ordinal) ||
                !string.Equals(row.TargetCrossplatformId, draft.TargetCrossplatformId, StringComparison.Ordinal) ||
                row.ExpectedEntityId != draft.ExpectedEntityId ||
                !string.Equals(row.ExpectedWorldId, draft.ExpectedWorldId, StringComparison.Ordinal) ||
                !string.Equals(row.DestinationWorldId, draft.Destination.WorldId, StringComparison.Ordinal) ||
                row.DestinationX != draft.Destination.X || row.DestinationY != draft.Destination.Y ||
                row.DestinationZ != draft.Destination.Z || row.DestinationYaw != draft.Destination.Yaw ||
                !string.Equals(row.ReservationId, draft.ReservationId, StringComparison.Ordinal) ||
                !string.Equals(row.ActorKind, draft.ActorKind, StringComparison.Ordinal) ||
                !string.Equals(row.ActorId, draft.ActorId, StringComparison.Ordinal) ||
                !string.Equals(row.CorrelationId, draft.CorrelationId, StringComparison.Ordinal) ||
                row.CreatedAtUtc != draft.CreatedAtUtc.ToUnixTimeMilliseconds())
            {
                throw new CommunityConflictException();
            }
        }

        private static bool FriendshipExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string first,
            string second) => connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM friendships
                  WHERE member_a_crossplatform_id = @First AND member_b_crossplatform_id = @Second;",
                new { First = first, Second = second },
                transaction) != 0;

        private static FriendRequestRow GetFriendRequest(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string requestId) => connection.QuerySingleOrDefault<FriendRequestRow>(
                FriendRequestSelect + " WHERE request_id = @RequestId;",
                new { RequestId = requestId },
                transaction) ?? throw new CommunityNotFoundException();

        private static TeleportFriendRequestRow GetTeleportFriendRequest(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string requestId) => connection.QuerySingleOrDefault<TeleportFriendRequestRow>(
                TeleportFriendRequestSelect + " WHERE request_id = @RequestId;",
                new { RequestId = requestId },
                transaction) ?? throw new CommunityNotFoundException();

        private bool ExecuteTeleportFriendRequestTransition(
            string requestId,
            string responderCrossplatformId,
            TeleportFriendRequestState state,
            string? teleportOperationId,
            DateTimeOffset respondedAtUtc,
            string expiryPredicate)
        {
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE teleport_friend_requests
                  SET state = @State, teleport_operation_id = @TeleportOperationId,
                      responded_at_utc = @RespondedAtUtc, row_version = row_version + 1
                  WHERE request_id = @RequestId
                    AND target_crossplatform_id = @ResponderCrossplatformId
                    AND state = 'Pending'
                    AND " + expiryPredicate + ";",
                new
                {
                    RequestId = requestId,
                    ResponderCrossplatformId = responderCrossplatformId,
                    State = state.ToString(),
                    TeleportOperationId = teleportOperationId,
                    RespondedAtUtc = respondedAtUtc.ToUnixTimeMilliseconds()
                }) == 1;
        }

        private static OperationRow GetOperation(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string operationId) => connection.QuerySingleOrDefault<OperationRow>(
                OperationSelect + " WHERE operation_id = @OperationId;",
                new { OperationId = operationId },
                transaction) ?? throw new CommunityNotFoundException();

        private static TeleportSettings ToSettings(SettingsRow row) => new TeleportSettings(
            Parse<TeleportKind>(row.Kind),
            row.Enabled != 0,
            row.MaxHomes,
            TimeSpan.FromMilliseconds(row.CooldownMs),
            TimeSpan.FromMilliseconds(row.GlobalCooldownMs),
            row.DenyDuringBloodMoon != 0,
            row.FeeAmount,
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion);

        private static PlayerHome ToHome(HomeRow row) => new PlayerHome(
            row.HomeId,
            row.CrossplatformId,
            row.Name,
            Position(row.WorldId, row.X, row.Y, row.Z, row.Yaw),
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion);

        private static City ToCity(CityRow row) => new City(
            row.CityId,
            row.Name,
            row.Description,
            row.Enabled != 0,
            Position(row.WorldId, row.X, row.Y, row.Z, row.Yaw),
            row.SortOrder,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion);

        private static Friendship ToFriendship(FriendshipRow row) => new Friendship(
            row.FriendshipId,
            row.MemberACrossplatformId,
            row.MemberBCrossplatformId,
            row.CreatedByCrossplatformId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.AcceptedAtUtc));

        private static FriendRequest ToFriendRequest(FriendRequestRow row) => new FriendRequest(
            row.RequestId,
            row.RequesterCrossplatformId,
            row.TargetCrossplatformId,
            Parse<FriendRequestState>(row.State),
            row.FriendshipId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresAtUtc),
            row.RespondedAtUtc.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.RespondedAtUtc.Value)
                : null,
            row.RowVersion);

        private static TeleportFriendRequest ToTeleportFriendRequest(TeleportFriendRequestRow row) =>
            new TeleportFriendRequest(
                row.RequestId,
                row.IdempotencyKey,
                row.RequesterCrossplatformId,
                row.RequesterEntityId,
                row.RequesterWorldId,
                row.TargetCrossplatformId,
                row.TargetEntityId,
                row.TargetWorldId,
                Parse<TeleportFriendRequestState>(row.State),
                row.TeleportOperationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresAtUtc),
                row.RespondedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.RespondedAtUtc.Value)
                    : null,
                row.RowVersion);

        private static PlayerReturnPoint ToReturnPoint(ReturnPointRow row) => new PlayerReturnPoint(
            row.CrossplatformId,
            row.SourceOperationId,
            Position(row.WorldId, row.X, row.Y, row.Z, row.Yaw),
            DateTimeOffset.FromUnixTimeMilliseconds(row.SavedAtUtc),
            row.RowVersion);

        private static TeleportOperation ToOperation(OperationRow row)
        {
            var draft = new TeleportOperationDraft(
                row.OperationId,
                Parse<TeleportKind>(row.Kind),
                row.CrossplatformId,
                row.TargetCrossplatformId,
                row.ExpectedEntityId,
                row.ExpectedWorldId,
                Position(
                    row.DestinationWorldId,
                    row.DestinationX,
                    row.DestinationY,
                    row.DestinationZ,
                    row.DestinationYaw),
                row.IdempotencyKey,
                row.ReservationId,
                row.ActorKind,
                row.ActorId,
                row.CorrelationId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc));
            var origin = row.OriginWorldId == null
                ? null
                : Position(
                    row.OriginWorldId,
                    row.OriginX!.Value,
                    row.OriginY!.Value,
                    row.OriginZ!.Value,
                    row.OriginYaw!.Value);
            return new TeleportOperation(
                draft,
                origin,
                Parse<TeleportOperationState>(row.State),
                row.ErrorCode,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.CompletedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value)
                    : null,
                row.RowVersion);
        }

        private static WorldPosition Position(
            string worldId,
            double x,
            double y,
            double z,
            double yaw) => new WorldPosition(worldId, x, y, z, yaw);

        private static void EnsureTeleportFriendRequestMatches(
            TeleportFriendRequestRow existing,
            TeleportFriendRequest request)
        {
            if (!string.Equals(existing.RequestId, request.RequestId, StringComparison.Ordinal) ||
                !string.Equals(existing.RequesterCrossplatformId, request.RequesterCrossplatformId, StringComparison.Ordinal) ||
                existing.RequesterEntityId != request.RequesterEntityId ||
                !string.Equals(existing.RequesterWorldId, request.RequesterWorldId, StringComparison.Ordinal) ||
                !string.Equals(existing.TargetCrossplatformId, request.TargetCrossplatformId, StringComparison.Ordinal) ||
                existing.TargetEntityId != request.TargetEntityId ||
                !string.Equals(existing.TargetWorldId, request.TargetWorldId, StringComparison.Ordinal) ||
                existing.CreatedAtUtc != request.CreatedAtUtc.ToUnixTimeMilliseconds() ||
                existing.ExpiresAtUtc != request.ExpiresAtUtc.ToUnixTimeMilliseconds())
            {
                throw new CommunityConflictException();
            }
        }

        private static (string First, string Second) CanonicalPair(string first, string second)
        {
            first = RequireText(first, nameof(first));
            second = RequireText(second, nameof(second));
            if (string.Equals(first, second, StringComparison.Ordinal))
                throw new ArgumentException("Friendship members must differ.", nameof(second));
            return string.CompareOrdinal(first, second) < 0
                ? (first, second)
                : (second, first);
        }

        private static long ToMilliseconds(TimeSpan value) => checked((long)value.TotalMilliseconds);

        private static T Parse<T>(string value) where T : struct, Enum =>
            (T)Enum.Parse(typeof(T), value, ignoreCase: false);

        private static string RequireText(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value!.Trim();
        }

        private static string? OptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void RequireOperationKind(TeleportKind value, string parameterName)
        {
            RequireDefined(value, parameterName);
            if (value == TeleportKind.Global) throw new ArgumentOutOfRangeException(parameterName);
        }

        private sealed class SettingsRow
        {
            public string Kind { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public int? MaxHomes { get; set; }
            public long CooldownMs { get; set; }
            public long GlobalCooldownMs { get; set; }
            public int DenyDuringBloodMoon { get; set; }
            public long FeeAmount { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class HomeRow
        {
            public string HomeId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Yaw { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class CityRow
        {
            public string CityId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public string WorldId { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Yaw { get; set; }
            public int SortOrder { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class FriendRequestRow
        {
            public string RequestId { get; set; } = string.Empty;
            public string RequesterCrossplatformId { get; set; } = string.Empty;
            public string TargetCrossplatformId { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string? FriendshipId { get; set; }
            public long CreatedAtUtc { get; set; }
            public long ExpiresAtUtc { get; set; }
            public long? RespondedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class FriendshipRow
        {
            public string FriendshipId { get; set; } = string.Empty;
            public string MemberACrossplatformId { get; set; } = string.Empty;
            public string MemberBCrossplatformId { get; set; } = string.Empty;
            public string CreatedByCrossplatformId { get; set; } = string.Empty;
            public long AcceptedAtUtc { get; set; }
        }

        private sealed class TeleportFriendRequestRow
        {
            public string RequestId { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public string RequesterCrossplatformId { get; set; } = string.Empty;
            public int RequesterEntityId { get; set; }
            public string RequesterWorldId { get; set; } = string.Empty;
            public string TargetCrossplatformId { get; set; } = string.Empty;
            public int TargetEntityId { get; set; }
            public string TargetWorldId { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string? TeleportOperationId { get; set; }
            public long CreatedAtUtc { get; set; }
            public long ExpiresAtUtc { get; set; }
            public long? RespondedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class CooldownRow
        {
            public string Kind { get; set; } = string.Empty;
            public long AvailableAtUtc { get; set; }
        }

        private sealed class ReturnPointRow
        {
            public string CrossplatformId { get; set; } = string.Empty;
            public string SourceOperationId { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double Yaw { get; set; }
            public long SavedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class OperationRow
        {
            public string OperationId { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public string? TargetCrossplatformId { get; set; }
            public int ExpectedEntityId { get; set; }
            public string ExpectedWorldId { get; set; } = string.Empty;
            public string DestinationWorldId { get; set; } = string.Empty;
            public double DestinationX { get; set; }
            public double DestinationY { get; set; }
            public double DestinationZ { get; set; }
            public double DestinationYaw { get; set; }
            public string? OriginWorldId { get; set; }
            public double? OriginX { get; set; }
            public double? OriginY { get; set; }
            public double? OriginZ { get; set; }
            public double? OriginYaw { get; set; }
            public string State { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? ReservationId { get; set; }
            public string ActorKind { get; set; } = string.Empty;
            public string ActorId { get; set; } = string.Empty;
            public string? CorrelationId { get; set; }
            public string? ErrorCode { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
