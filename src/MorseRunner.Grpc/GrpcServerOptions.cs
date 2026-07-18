namespace MorseRunner.Grpc;

public sealed record GrpcServerOptions
{
    public required string AuthenticationToken { get; init; }

    public TimeSpan ControlLeaseDuration { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ControlLeaseGracePeriod { get; init; } = TimeSpan.FromSeconds(2);

    public bool AllowForcedTakeover { get; init; }

    public int MaximumReceiveMessageSize { get; init; } = 1024 * 1024;

    public int MaximumSendMessageSize { get; init; } = 4 * 1024 * 1024;
}
