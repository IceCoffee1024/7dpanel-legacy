using System;
using System.Web.Http.ExceptionHandling;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal sealed class OwinUnhandledExceptionLogger : ExceptionLogger
    {
        private readonly Action<string> log;

        public OwinUnhandledExceptionLogger(Action<string>? log)
        {
            this.log = log ?? (_ => { });
        }

        public override bool ShouldLog(ExceptionLoggerContext context) =>
            !context.CallsHandler && base.ShouldLog(context);

        public override void Log(ExceptionLoggerContext context)
        {
            var requestId = context.Request?.Headers.Contains(RequestCorrelationMiddleware.HeaderName) == true
                ? context.Request.Headers.GetValues(RequestCorrelationMiddleware.HeaderName).FirstOrDefault()
                : null;
            log(
                "Unhandled 7DPanel API exception. RequestId=" +
                (requestId ?? "<unknown>") +
                ": " +
                context.Exception);
        }
    }
}