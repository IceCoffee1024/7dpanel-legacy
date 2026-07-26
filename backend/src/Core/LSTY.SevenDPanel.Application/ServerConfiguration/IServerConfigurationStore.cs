namespace LSTY.SevenDPanel.Application.ServerConfiguration
{
    public interface IServerConfigurationStore
    {
        ServerConfigurationSnapshot Read(ServerConfigurationFieldCatalog catalog);

        ServerConfigurationUpdateResult Update(
            UpdateServerConfigurationRequest request,
            ServerConfigurationFieldCatalog catalog);
    }
}
