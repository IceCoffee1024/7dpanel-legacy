using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LSTY.SevenDPanel.Adapters.Local.WorldOperations;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class WorldChangeSetStoreTests
    {
        [Fact]
        public void Blob_store_round_trips_verified_content_without_exposing_a_path()
        {
            using var fixture = new BlobFixture();
            var content = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
            var resourceId = LocalWorldChangeSetBlobStore.CreateStorageResourceId();
            var expectedHash = Hash(content);

            var receipt = fixture.Store.Write(
                new WorldChangeSetBlobDraft(resourceId, expectedHash, content));
            var read = fixture.Store.Read(resourceId, expectedHash);

            Assert.Equal(resourceId, receipt.StorageResourceId);
            Assert.Equal(expectedHash, receipt.ContentHash);
            Assert.Equal(content.LongLength, receipt.ByteCount);
            Assert.Equal(content, read.Content);
            Assert.Equal(expectedHash, read.ContentHash);
            Assert.DoesNotContain(
                receipt.GetType().GetProperties(),
                property => property.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            property.Name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Single(Directory.GetFiles(fixture.RootPath));
        }

        [Fact]
        public void Blob_store_rejects_untrusted_ids_and_hash_mismatch_without_leaving_a_file()
        {
            using var fixture = new BlobFixture();
            var content = new byte[] { 1, 2, 3 };

            Assert.Throws<ArgumentException>(() => fixture.Store.Write(
                new WorldChangeSetBlobDraft("client-selected", Hash(content), content)));
            Assert.Throws<InvalidDataException>(() => fixture.Store.Write(
                new WorldChangeSetBlobDraft(
                    LocalWorldChangeSetBlobStore.CreateStorageResourceId(),
                    Hash(new byte[] { 9 }),
                    content)));

            Assert.Empty(Directory.GetFiles(fixture.RootPath));
        }

        [Fact]
        public void Blob_store_detects_corruption_and_preserves_the_existing_resource()
        {
            using var fixture = new BlobFixture();
            var original = new byte[] { 3, 1, 4, 1, 5, 9 };
            var resourceId = LocalWorldChangeSetBlobStore.CreateStorageResourceId();
            var originalHash = Hash(original);
            fixture.Store.Write(new WorldChangeSetBlobDraft(resourceId, originalHash, original));
            var path = Assert.Single(Directory.GetFiles(fixture.RootPath));

            Assert.Throws<IOException>(() => fixture.Store.Write(
                new WorldChangeSetBlobDraft(resourceId, Hash(new byte[] { 2, 7 }), new byte[] { 2, 7 })));
            Assert.Equal(original, fixture.Store.Read(resourceId, originalHash).Content);

            var stored = File.ReadAllBytes(path);
            stored[stored.Length - 1] ^= 0xff;
            File.WriteAllBytes(path, stored);
            Assert.ThrowsAny<InvalidDataException>(() => fixture.Store.Read(resourceId, originalHash));
        }

        [Fact]
        public void Blob_store_requires_an_absolute_non_reparse_root()
        {
            Assert.Throws<ArgumentException>(() => new LocalWorldChangeSetBlobStore("relative-root"));
        }

        private static string Hash(byte[] content)
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(content).Select(value => value.ToString("x2")));
        }

        private sealed class BlobFixture : IDisposable
        {
            private readonly string rootPath = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-world-change-set-tests",
                Guid.NewGuid().ToString("N"));

            public BlobFixture()
            {
                Directory.CreateDirectory(rootPath);
                Store = new LocalWorldChangeSetBlobStore(rootPath);
            }

            public string RootPath => rootPath;
            public LocalWorldChangeSetBlobStore Store { get; }

            public void Dispose()
            {
                if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
