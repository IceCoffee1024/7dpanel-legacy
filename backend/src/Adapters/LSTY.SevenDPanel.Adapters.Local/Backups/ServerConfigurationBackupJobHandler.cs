using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Application.Backups;

namespace LSTY.SevenDPanel.Adapters.Local.Backups
{
    public sealed class ServerConfigurationBackupJobHandler
    {
        public const string EmptyFileListError = "server_configuration_file_list_empty";
        public const string SourceUnavailableError = "server_configuration_source_unavailable";

        private readonly ApprovedStorageRoots roots;
        private readonly FileSystemBackupArchiveStore archives;
        private readonly IReadOnlyCollection<string> approvedRelativeFiles;

        public ServerConfigurationBackupJobHandler(
            ApprovedStorageRoots roots,
            FileSystemBackupArchiveStore archives,
            IReadOnlyCollection<string> approvedRelativeFiles)
        {
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
            this.archives = archives ?? throw new ArgumentNullException(nameof(archives));
            if (approvedRelativeFiles == null)
                throw new ArgumentNullException(nameof(approvedRelativeFiles));

            var normalized = approvedRelativeFiles
                .Select(roots.NormalizeServerConfigurationRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalized.Length == 0)
                throw new ArgumentException(EmptyFileListError, nameof(approvedRelativeFiles));
            this.approvedRelativeFiles = normalized;
        }

        public CompletedBackup Execute(Guid sourceJobId, DateTimeOffset createdAtUtc)
        {
            foreach (var relativePath in approvedRelativeFiles)
            {
                try
                {
                    var fullPath = roots.ResolveServerConfigurationFile(relativePath);
                    if (!System.IO.File.Exists(fullPath))
                        throw new System.IO.FileNotFoundException(
                            "A required server configuration file is missing.",
                            fullPath);
                }
                catch (Exception exception) when (!(exception is BackupArchiveException))
                {
                    throw new BackupArchiveException(SourceUnavailableError, exception);
                }
            }

            return archives.CreateServerConfigurationArchive(
                sourceJobId,
                approvedRelativeFiles,
                createdAtUtc);
        }
    }
}
