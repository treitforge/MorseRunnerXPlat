using Grpc.Core;
using MorseRunner.Contracts.V1;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.Grpc;

public sealed class CatalogGrpcService(
    GrpcRequestGuard guard) : CatalogService.CatalogServiceBase
{
    public override Task<ListContestsResponse> ListContests(
        ListContestsRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        var response = new ListContestsResponse();
        response.Contests.Add(
            ContestCatalog.All.Select(TransportMapper.ToTransport));
        return Task.FromResult(response);
    }

    public override Task<GetContestDefinitionResponse> GetContestDefinition(
        GetContestDefinitionRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(
                new GetContestDefinitionResponse
                {
                    Contest = TransportMapper.ToTransport(
                        ContestCatalog.Get(new(request.ContestId))),
                });
        }
        catch (InvalidOperationException)
        {
            throw new RpcException(
                new Status(
                    StatusCode.NotFound,
                    $"Contest '{request.ContestId}' was not found."));
        }
    }

    public override Task<GetDefaultSettingsResponse> GetDefaultSettings(
        GetDefaultSettingsRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GetDefaultSettingsResponse
            {
                Settings = TransportMapper.ToTransport(
                    SessionSettings.CreateDefault(request.Seed)),
            });
    }

    public override Task<GetDataStatusResponse> GetDataStatus(
        GetDataStatusRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        context.CancellationToken.ThrowIfCancellationRequested();
        var catalog = new PackagedDataCatalog();
        return Task.FromResult(
            new GetDataStatusResponse
            {
                Ready = catalog.FileNames.Count > 0,
                PackagedFileCount = catalog.FileNames.Count,
                Status = catalog.FileNames.Count > 0
                    ? "ready"
                    : "packaged-data-missing",
            });
    }
}
