using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Diagnostics;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "SevenDays")]
    public sealed class ClientInfoBoundaryProbeContractTests
    {
        [Fact]
        public void Resolver_prefers_one_available_crossplatform_identity_before_platform_fallback()
        {
            var platformMatch = new Candidate("cross-other", "stable-id");
            var crossplatformMatch = new Candidate("stable-id", "platform-other");

            var selected = ClientInfoBoundaryProbe.FindPreferredIdentityMatch(
                new[] { platformMatch, crossplatformMatch },
                "stable-id",
                candidate => candidate.CrossplatformId,
                candidate => candidate.PlatformId,
                candidate => candidate.IsAvailable);
            var fallback = ClientInfoBoundaryProbe.FindPreferredIdentityMatch(
                new[] { platformMatch },
                "stable-id",
                candidate => candidate.CrossplatformId,
                candidate => candidate.PlatformId,
                candidate => candidate.IsAvailable);
            var ambiguous = ClientInfoBoundaryProbe.FindPreferredIdentityMatch(
                new[] { crossplatformMatch, new Candidate("stable-id", "another-platform") },
                "stable-id",
                candidate => candidate.CrossplatformId,
                candidate => candidate.PlatformId,
                candidate => candidate.IsAvailable);
            var disconnecting = ClientInfoBoundaryProbe.FindPreferredIdentityMatch(
                new[] { new Candidate("stable-id", "platform", false) },
                "stable-id",
                candidate => candidate.CrossplatformId,
                candidate => candidate.PlatformId,
                candidate => candidate.IsAvailable);

            Assert.Same(crossplatformMatch, selected);
            Assert.Same(platformMatch, fallback);
            Assert.Null(ambiguous);
            Assert.Null(disconnecting);
        }

        [Fact]
        public async Task Every_probe_dispatches_and_re_resolves_without_retaining_a_client_handle()
        {
            var resolutions = new List<string>();
            var dispatched = new List<string>();
            var probe = new ClientInfoBoundaryProbe(
                "  EOS_123  ",
                (operation, action, timeout, cancellationToken) =>
                {
                    dispatched.Add(operation);
                    Assert.Equal(TimeSpan.FromSeconds(3), timeout);
                    Assert.False(cancellationToken.IsCancellationRequested);
                    return Task.FromResult(action());
                },
                identity =>
                {
                    resolutions.Add("identity:" + identity);
                    return new ClientInfoIdentitySnapshot(42, "Survivor", "EOS_123", "Steam_456");
                },
                identity =>
                {
                    resolutions.Add("position:" + identity);
                    return new ClientInfoPositionSnapshot(1.25f, 64f, -9.5f);
                },
                (identity, message) =>
                {
                    resolutions.Add("reply:" + identity + ":" + message);
                    return true;
                });

            var identityResult = await probe.ProbeIdentityAsync(TestContext.Current.CancellationToken);
            var positionResult = await probe.ProbeCurrentPositionAsync(TestContext.Current.CancellationToken);
            var replyResult = await probe.ProbePrivateReplyAsync(
                "probe-token",
                TestContext.Current.CancellationToken);

            Assert.Equal(ClientInfoBoundaryProbeStatus.Passed, identityResult.Status);
            Assert.Equal("identity", identityResult.ProbeName);
            Assert.Equal("entityId=42; playerName=Survivor", identityResult.Detail);
            Assert.Equal(ClientInfoBoundaryProbeStatus.Passed, positionResult.Status);
            Assert.Equal("x=1.25; y=64; z=-9.5", positionResult.Detail);
            Assert.Equal(ClientInfoBoundaryProbeStatus.Passed, replyResult.Status);
            Assert.Equal(
                new[]
                {
                    "identity:EOS_123",
                    "position:EOS_123",
                    "reply:EOS_123:probe-token"
                },
                resolutions);
            Assert.Equal(3, dispatched.Count);
        }

        [Fact]
        public async Task Unavailable_boundaries_are_skipped_and_boundary_exceptions_are_failed()
        {
            var unavailable = new ClientInfoBoundaryProbe(
                "stable-id",
                (_, action, _, _) => Task.FromResult(action()),
                _ => null,
                _ => null,
                (_, _) => false);
            var failing = new ClientInfoBoundaryProbe(
                "stable-id",
                (_, action, _, _) => Task.FromResult(action()),
                _ => throw new InvalidOperationException("native identity failure"),
                _ => throw new InvalidOperationException("native position failure"),
                (_, _) => throw new InvalidOperationException("native reply failure"));

            Assert.Equal(
                ClientInfoBoundaryProbeStatus.Skipped,
                (await unavailable.ProbeIdentityAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Equal(
                ClientInfoBoundaryProbeStatus.Skipped,
                (await unavailable.ProbeCurrentPositionAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Equal(
                ClientInfoBoundaryProbeStatus.Skipped,
                (await unavailable.ProbePrivateReplyAsync("probe", TestContext.Current.CancellationToken)).Status);
            Assert.Equal(
                "empty_message",
                (await unavailable.ProbePrivateReplyAsync(" ", TestContext.Current.CancellationToken)).Code);

            Assert.Equal(
                ClientInfoBoundaryProbeStatus.Failed,
                (await failing.ProbeIdentityAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Equal(
                ClientInfoBoundaryProbeStatus.Failed,
                (await failing.ProbeCurrentPositionAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Equal(
                ClientInfoBoundaryProbeStatus.Failed,
                (await failing.ProbePrivateReplyAsync("probe", TestContext.Current.CancellationToken)).Status);
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "SevenDays")]

        private sealed class Candidate
        {
            public Candidate(string? crossplatformId, string? platformId, bool isAvailable = true)
            {
                CrossplatformId = crossplatformId;
                PlatformId = platformId;
                IsAvailable = isAvailable;
            }

            public string? CrossplatformId { get; }
            public string? PlatformId { get; }
            public bool IsAvailable { get; }
        }
    }
}
