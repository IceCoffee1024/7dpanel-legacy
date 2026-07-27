using System;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Economy;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community
{
    public sealed class SqliteChargedTeleportOperationStore : IChargedTeleportOperationStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteChargedTeleportOperationStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public ChargedTeleportOperationResult ReserveAndCreate(
            FundsReservationDraft reservation,
            TeleportOperationDraft operation)
        {
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var reserved = SqliteEconomyLedgerStore.TryReserve(connection, transaction, reservation);
            if (reserved.Status != FundsReservationStatus.Reserved &&
                reserved.Status != FundsReservationStatus.Captured)
            {
                transaction.Commit();
                return new ChargedTeleportOperationResult(reserved, null);
            }

            var created = SqliteCommunityStore.CreateTeleportOperation(connection, transaction, operation);
            transaction.Commit();
            return new ChargedTeleportOperationResult(reserved, created);
        }
    }
}
