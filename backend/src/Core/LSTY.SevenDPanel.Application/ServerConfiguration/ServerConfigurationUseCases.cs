using System;

namespace LSTY.SevenDPanel.Application.ServerConfiguration
{
    public sealed class GetServerConfigurationUseCase
    {
        private readonly IServerConfigurationStore store;
        private readonly ServerConfigurationFieldCatalog catalog;

        public GetServerConfigurationUseCase(IServerConfigurationStore store, ServerConfigurationFieldCatalog catalog)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ServerConfigurationSnapshot Execute() => store.Read(catalog);
    }

    public sealed class UpdateServerConfigurationUseCase
    {
        private readonly IServerConfigurationStore store;
        private readonly ServerConfigurationFieldCatalog catalog;

        public UpdateServerConfigurationUseCase(IServerConfigurationStore store, ServerConfigurationFieldCatalog catalog)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public ServerConfigurationUpdateResult Execute(UpdateServerConfigurationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key) || !catalog.TryGet(request.Key, out var definition))
                return Failed(ServerConfigurationUpdateStatus.UnknownField, request?.Version);
            if (!definition.Editable)
                return Failed(ServerConfigurationUpdateStatus.ReadOnly, request.Version, definition.RestartRequired);

            var current = store.Read(catalog);
            if (!string.Equals(current.Version, request.Version, StringComparison.Ordinal))
                return Failed(ServerConfigurationUpdateStatus.Conflict, current.Version, definition.RestartRequired);

            return store.Update(request, catalog);
        }

        private static ServerConfigurationUpdateResult Failed(
            ServerConfigurationUpdateStatus status,
            string? version,
            bool restartRequired = false)
        {
            return new ServerConfigurationUpdateResult(status, version ?? string.Empty, null, restartRequired);
        }
    }
}
