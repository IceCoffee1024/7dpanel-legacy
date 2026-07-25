using System;

namespace LSTY.SevenDPanel.Application
{
    public class ServerOperationException : Exception
    {
        protected ServerOperationException(string failureCode, Exception? innerException = null)
            : base(failureCode, innerException)
        {
            FailureCode = failureCode;
        }

        public string FailureCode { get; }
    }

    public sealed class ServerOperationConfirmationRequiredException : ServerOperationException
    {
        public ServerOperationConfirmationRequiredException()
            : base("confirmation_required")
        {
        }
    }

    public sealed class ServerOperationBusyException : ServerOperationException
    {
        public ServerOperationBusyException()
            : base("operation_in_progress")
        {
        }
    }

    public sealed class ServerOperationAuditUnavailableException : ServerOperationException
    {
        public ServerOperationAuditUnavailableException(Exception innerException)
            : base("audit_unavailable", innerException)
        {
        }
    }

    public sealed class ServerOperationFailedException : ServerOperationException
    {
        public ServerOperationFailedException(string failureCode)
            : base(RequireFailureCode(failureCode))
        {
        }

        private static string RequireFailureCode(string failureCode)
        {
            if (!ServerOperationCodeContract.IsFailureCode(failureCode))
                throw new ArgumentException("The server operation failure code is not approved.", nameof(failureCode));

            return failureCode;
        }
    }
}
