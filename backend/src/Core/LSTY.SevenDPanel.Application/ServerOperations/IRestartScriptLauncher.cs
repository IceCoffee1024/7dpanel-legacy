using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IRestartScriptLauncher
    {
        DateTimeOffset StartConfiguredScript();
    }
}
