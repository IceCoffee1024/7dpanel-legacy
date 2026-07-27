using System;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public interface IChargedTeleportOperationStore
    {
        ChargedTeleportOperationResult ReserveAndCreate(
            FundsReservationDraft reservation,
            TeleportOperationDraft operation);
    }

    public sealed class ChargedTeleportOperationResult
    {
        public ChargedTeleportOperationResult(
            FundsReservationResult reservation,
            TeleportOperation? operation)
        {
            Reservation = reservation ?? throw new ArgumentNullException(nameof(reservation));
            Operation = operation;
        }

        public FundsReservationResult Reservation { get; }
        public TeleportOperation? Operation { get; }
    }
}
