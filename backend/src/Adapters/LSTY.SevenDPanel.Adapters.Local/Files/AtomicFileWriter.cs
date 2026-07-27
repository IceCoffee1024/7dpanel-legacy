using System;
using System.IO;

namespace LSTY.SevenDPanel.Adapters.Local.Files
{
    public sealed class AtomicFileWriter
    {
        private readonly ApprovedStorageRoots roots;
        private readonly object writeGate = new object();

        public AtomicFileWriter(ApprovedStorageRoots roots)
        {
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
        }

        public T Write<T>(string relativeResourceId, Func<string, T> writeAndValidate)
        {
            if (writeAndValidate == null) throw new ArgumentNullException(nameof(writeAndValidate));
            var destination = roots.ResolveBackupResource(relativeResourceId);
            var directory = Path.GetDirectoryName(destination) ??
                throw new IOException("backup_destination_invalid");
            Directory.CreateDirectory(directory);
            destination = roots.ResolveBackupResource(relativeResourceId);
            lock (writeGate)
            {
                if (File.Exists(destination))
                    throw new IOException("backup_target_exists");

                var temporaryPath = Path.Combine(
                    directory,
                    "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                var published = false;
                try
                {
                    var result = writeAndValidate(temporaryPath);
                    if (!File.Exists(temporaryPath))
                        throw new IOException("backup_temporary_file_missing");
                    File.Move(temporaryPath, destination);
                    published = true;
                    return result;
                }
                finally
                {
                    if (!published && File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
        }
    }
}
