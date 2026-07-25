using System;
using System.Diagnostics;
using System.IO;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Hosting
{
    public enum RestartScriptPlatform
    {
        Windows,
        Linux,
        Unsupported
    }

    public sealed class RestartScriptLauncher : IRestartScriptLauncher
    {
        private readonly RestartScriptOptions? options;
        private readonly Func<RestartScriptPlatform> platform;
        private readonly Func<string, bool> fileExists;
        private readonly Func<ProcessStartInfo, IDisposable?> startProcess;
        private readonly Func<DateTimeOffset> utcClock;

        public RestartScriptLauncher(RestartScriptOptions options)
            : this(
                options,
                DetectPlatform,
                File.Exists,
                startInfo => Process.Start(startInfo),
                () => DateTimeOffset.UtcNow)
        {
        }

        public RestartScriptLauncher(
            RestartScriptOptions? options,
            Func<RestartScriptPlatform> platform,
            Func<string, bool> fileExists,
            Func<ProcessStartInfo, IDisposable?> startProcess,
            Func<DateTimeOffset> utcClock)
        {
            this.options = options;
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
            this.startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public DateTimeOffset StartConfiguredScript()
        {
            try
            {
                if (options == null)
                    throw new ServerOperationFailedException("restart_script_not_configured");

                string scriptPath;
                ProcessStartInfo startInfo;
                switch (platform())
                {
                    case RestartScriptPlatform.Windows:
                        scriptPath = options.WindowsScript;
                        startInfo = CreateStartInfo(
                            "cmd.exe",
                            "/d /s /c \"\"" + scriptPath + "\"\"");
                        break;
                    case RestartScriptPlatform.Linux:
                        scriptPath = options.LinuxScript;
                        startInfo = CreateStartInfo(
                            "/bin/sh",
                            "'" + scriptPath + "'");
                        break;
                    default:
                        throw new ServerOperationFailedException(
                            "restart_script_platform_unsupported");
                }

                if (string.IsNullOrWhiteSpace(scriptPath))
                    throw new ServerOperationFailedException("restart_script_not_configured");
                if (!fileExists(scriptPath))
                    throw new ServerOperationFailedException("restart_script_missing");

                using (var processHandle = startProcess(startInfo))
                {
                    if (processHandle == null)
                        throw new ServerOperationFailedException("restart_script_start_failed");
                }

                return utcClock();
            }
            catch (ServerOperationFailedException)
            {
                throw;
            }
            catch
            {
                throw new ServerOperationFailedException("restart_script_start_failed");
            }
        }

        private ProcessStartInfo CreateStartInfo(string fileName, string arguments)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = options!.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
        }

        private static RestartScriptPlatform DetectPlatform()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return RestartScriptPlatform.Windows;
                case PlatformID.Unix:
                    return RestartScriptPlatform.Linux;
                default:
                    return RestartScriptPlatform.Unsupported;
            }
        }
    }
}
