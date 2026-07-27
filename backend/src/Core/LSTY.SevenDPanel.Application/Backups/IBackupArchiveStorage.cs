using System;
using System.IO;

namespace LSTY.SevenDPanel.Application.Backups
{
    public interface IBackupArchiveStorage
    {
        Stream OpenRead(BackupArtifact artifact);
        void Delete(BackupArtifact artifact);
    }

    public sealed class PreparedBackupDownload : IDisposable
    {
        public PreparedBackupDownload(
            string attachmentFileName,
            long contentLength,
            Stream content)
        {
            if (string.IsNullOrWhiteSpace(attachmentFileName))
                throw new ArgumentException("An attachment file name is required.", nameof(attachmentFileName));
            if (contentLength < 0) throw new ArgumentOutOfRangeException(nameof(contentLength));
            AttachmentFileName = attachmentFileName;
            ContentLength = contentLength;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string AttachmentFileName { get; }
        public long ContentLength { get; }
        public Stream Content { get; }

        public void Dispose() => Content.Dispose();
    }
}
