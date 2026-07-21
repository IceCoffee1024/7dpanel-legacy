namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public interface IPanelCredentialStore
    {
        bool TryVerify(string username, string password, out PanelUserIdentity identity);

        bool TryGetActive(string subject, out PanelUserIdentity identity);
    }
}
