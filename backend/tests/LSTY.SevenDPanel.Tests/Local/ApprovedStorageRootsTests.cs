using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class ApprovedStorageRootsTests
    {
        [Theory]
        [InlineData("../outside.zip")]
        [InlineData("world/../../outside.zip")]
        [InlineData("/outside.zip")]
        [InlineData("C:\\outside.zip")]
        public void Backup_resources_reject_absolute_and_parent_paths(string resourceId)
        {
            using var directories = new TestDirectories();
            var roots = directories.CreateRoots();

            Assert.Throws<ArgumentException>(() => roots.ResolveBackupResource(resourceId));
        }

        [Fact]
        public void Current_world_is_fixed_by_bootstrap_and_cannot_be_selected_by_path()
        {
            using var directories = new TestDirectories();
            var roots = directories.CreateRoots();

            Assert.Equal(directories.World, roots.RequireCurrentWorldDirectory("Navezgane"));
            Assert.Throws<ArgumentException>(() => roots.RequireCurrentWorldDirectory("../Navezgane"));
            Assert.Throws<ArgumentException>(() => roots.RequireCurrentWorldDirectory("RWG"));
        }

        [Fact]
        public void Reparse_points_cannot_escape_an_approved_root()
        {
            if (Path.DirectorySeparatorChar != '\\') return;

            using var directories = new TestDirectories();
            var outside = Path.Combine(directories.Root, "outside");
            var junction = Path.Combine(directories.Backups, "escape");
            Directory.CreateDirectory(outside);
            CreateJunction(junction, outside);
            try
            {
                var roots = directories.CreateRoots();

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    roots.ResolveBackupResource("escape/archive.zip"));

                Assert.Equal("path_reparse_not_allowed", exception.Message);
            }
            finally
            {
                if (Directory.Exists(junction)) Directory.Delete(junction);
            }
        }

        [Theory]
        [InlineData("../evil.txt")]
        [InlineData("folder/../../evil.txt")]
        [InlineData("/absolute.txt")]
        [InlineData("C:\\absolute.txt")]
        public void Zip_entries_reject_traversal_and_absolute_names(string entryName)
        {
            Assert.Throws<ArgumentException>(() =>
                FileSystemBackupArchiveStore.ValidateEntryName(entryName));
        }

        [Fact]
        public void Atomic_writer_uses_a_temporary_sibling_and_cleans_it_after_publish()
        {
            using var directories = new TestDirectories();
            var roots = directories.CreateRoots();
            var writer = new AtomicFileWriter(roots);
            string? temporaryPath = null;

            var result = writer.Write("world/archive.zip", path =>
            {
                temporaryPath = path;
                File.WriteAllText(path, "archive");
                return 42;
            });

            var destination = roots.ResolveBackupResource("world/archive.zip");
            Assert.Equal(42, result);
            Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(temporaryPath));
            Assert.True(File.Exists(destination));
            Assert.False(File.Exists(temporaryPath));
        }

        [Fact]
        public void Atomic_writer_cleans_unpublished_temporary_files_after_failure()
        {
            using var directories = new TestDirectories();
            var roots = directories.CreateRoots();
            var writer = new AtomicFileWriter(roots);
            string? temporaryPath = null;

            Assert.Throws<IOException>(() => writer.Write<object>("world/failure.zip", path =>
            {
                temporaryPath = path;
                File.WriteAllText(path, "partial");
                throw new IOException("simulated write failure");
            }));

            Assert.False(File.Exists(temporaryPath));
            Assert.False(File.Exists(roots.ResolveBackupResource("world/failure.zip")));
        }

        [Fact]
        public async Task Atomic_writer_never_writes_one_target_concurrently()
        {
            using var directories = new TestDirectories();
            var writer = new AtomicFileWriter(directories.CreateRoots());
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var secondEntered = false;

            var first = Task.Run(() => writer.Write("world/single.zip", path =>
            {
                entered.Set();
                release.Wait(TestContext.Current.CancellationToken);
                File.WriteAllText(path, "first");
                return true;
            }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            var second = Task.Run(() => Assert.Throws<IOException>(() =>
                writer.Write("world/single.zip", path =>
                {
                    secondEntered = true;
                    File.WriteAllText(path, "second");
                    return true;
                })));

            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(secondEntered);
            release.Set();
            await first;
            await second;
        }

        [Fact]
        public async Task Failed_writer_does_not_release_a_target_lock_while_an_existing_waiter_is_active()
        {
            using var directories = new TestDirectories();
            var writer = new AtomicFileWriter(directories.CreateRoots());
            using var firstEntered = new ManualResetEventSlim(false);
            using var releaseFirst = new ManualResetEventSlim(false);
            using var secondEntered = new ManualResetEventSlim(false);
            using var releaseSecond = new ManualResetEventSlim(false);
            var thirdEntered = false;

            var first = Task.Run(() =>
            {
                try
                {
                    writer.Write<object>("world/retry.zip", path =>
                    {
                        firstEntered.Set();
                        releaseFirst.Wait(TestContext.Current.CancellationToken);
                        throw new IOException("first failed");
                    });
                }
                catch (IOException)
                {
                }
            });
            Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            var second = new Thread(() =>
            {
                try
                {
                    writer.Write("world/retry.zip", path =>
                    {
                        secondEntered.Set();
                        releaseSecond.Wait(TestContext.Current.CancellationToken);
                        File.WriteAllText(path, "second");
                        return true;
                    });
                }
                catch (IOException)
                {
                }
            });
            second.IsBackground = true;
            second.Start();
            Assert.True(SpinWait.SpinUntil(
                () => (second.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));
            releaseFirst.Set();
            await first;
            Assert.True(secondEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            var third = Task.Run(() =>
            {
                try
                {
                    writer.Write("world/retry.zip", path =>
                    {
                        thirdEntered = true;
                        File.WriteAllText(path, "third");
                        return true;
                    });
                }
                catch (IOException)
                {
                }
            });
            await Task.Delay(50, TestContext.Current.CancellationToken);
            var overlapped = thirdEntered;
            releaseSecond.Set();
            Assert.True(second.Join(TimeSpan.FromSeconds(5)));
            await third;

            Assert.False(overlapped);
        }

        private static void CreateJunction(string junction, string target)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /c mklink /J \"" + junction + "\" \"" + target + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Unable to start mklink.");
            process.WaitForExit();
            Assert.True(
                process.ExitCode == 0,
                "mklink failed: " + process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
        }
    }
}
