using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public sealed class ApiKeyCreateResult
    {
        private ApiKeyCreateResult(ApiKeyCreateStatus status, CreatedApiKey? createdApiKey)
        {
            Status = status;
            CreatedApiKey = createdApiKey;
        }

        public ApiKeyCreateStatus Status { get; }

        public CreatedApiKey? CreatedApiKey { get; }

        public static ApiKeyCreateResult Created(CreatedApiKey createdApiKey)
        {
            if (createdApiKey == null) throw new ArgumentNullException(nameof(createdApiKey));
            return new ApiKeyCreateResult(ApiKeyCreateStatus.Created, createdApiKey);
        }

        public static ApiKeyCreateResult Failed(ApiKeyCreateStatus status)
        {
            if (status == ApiKeyCreateStatus.Created)
                throw new ArgumentOutOfRangeException(nameof(status));
            return new ApiKeyCreateResult(status, null);
        }
    }
}