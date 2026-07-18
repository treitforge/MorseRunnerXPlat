using Grpc.Core;
using MorseRunner.Domain;

namespace MorseRunner.Grpc.Tests;

public sealed class ControlLeaseManagerTests
{
    [Fact]
    public void ExpiredLeaseCanBeAcquiredByAnotherClient()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));
        using var manager = new ControlLeaseManager(
            new GrpcServerOptions
            {
                AuthenticationToken = "test",
                ControlLeaseDuration = TimeSpan.FromSeconds(10),
                ControlLeaseGracePeriod = TimeSpan.FromSeconds(2),
            },
            clock);
        SessionId sessionId = SessionId.New();
        _ = manager.Acquire(sessionId, new("owner"));

        clock.Advance(TimeSpan.FromSeconds(13));
        ControlLease replacement = manager.Acquire(
            sessionId,
            new("replacement"));

        Assert.Equal(new ClientId("replacement"), replacement.OwningClientId);
        Assert.Equal(1, replacement.Revision);
    }

    [Fact]
    public void ForcedTakeoverIsDisabledWithoutExplicitCapability()
    {
        using var manager = new ControlLeaseManager(
            new GrpcServerOptions
            {
                AuthenticationToken = "test",
                AllowForcedTakeover = false,
            });
        SessionId sessionId = SessionId.New();
        _ = manager.Acquire(sessionId, new("owner"));

        RpcException exception = Assert.Throws<RpcException>(
            () => manager.Take(sessionId, new("other"), force: true));

        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
        Assert.Equal(
            DomainErrorCodes.UnsupportedCapability,
            exception.Trailers.GetValue("morse-error-code"));
    }
}
