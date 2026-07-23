namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public enum ApiKeyCreateStatus
    {
        Created,
        SubjectNotFound,
        InvalidName,
        InvalidExpiration,
        CapacityReached
    }
}