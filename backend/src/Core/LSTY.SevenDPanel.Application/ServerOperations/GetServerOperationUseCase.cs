using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetServerOperationUseCase
    {
        private readonly IServerOperationStore store;

        public GetServerOperationUseCase(IServerOperationStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public ServerOperationSnapshot? Execute(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("An operation identifier is required.", nameof(operationId));
            try { return store.Get(operationId); }
            catch (Exception exception) { throw new ServerOperationSourceUnavailableException(exception); }
        }
    }

    public sealed class ServerOperationSourceUnavailableException : Exception
    {
        public ServerOperationSourceUnavailableException(Exception innerException)
            : base("The server operation status source is unavailable.", innerException) { }
    }
}
