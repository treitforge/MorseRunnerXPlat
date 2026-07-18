using Grpc.Core;
using MorseRunner.Client;
using MorseRunner.Contracts.V1;

namespace MorseRunner.Grpc;

public sealed class EngineGrpcService(
    IMorseRunnerClient client,
    GrpcRequestGuard guard) : EngineService.EngineServiceBase
{
    public override async Task<GetEngineInfoResponse> GetEngineInfo(
        GetEngineInfoRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        Domain.EngineInfo info = await client.GetEngineInfoAsync(
            context.CancellationToken);
        string[] capabilities = info.Capabilities
            .Append("results")
            .Append("control-lease")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new()
        {
            EngineInfo = TransportMapper.ToTransport(
                info with { Capabilities = capabilities },
                isInProcess: false),
        };
    }

    public override async Task<GetHealthResponse> GetHealth(
        GetHealthRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        _ = await client.GetEngineInfoAsync(context.CancellationToken);
        return new() { Ready = true, Status = "ready" };
    }
}
