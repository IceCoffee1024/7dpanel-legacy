using System.Web.Http.ExceptionHandling;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal sealed class OwinPassThroughExceptionHandler : ExceptionHandler
    {
        public override void Handle(ExceptionHandlerContext context)
        {
            context.Result = null;
        }
    }
}