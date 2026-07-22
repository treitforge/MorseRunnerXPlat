using Google.Protobuf;
using Grpc.Core;
using MorseRunner.Client;
using MorseRunner.Contracts.V1;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

namespace MorseRunner.Grpc;

public sealed class ResultsGrpcService(
    IMorseRunnerClient client,
    GrpcRequestGuard guard) : ResultsService.ResultsServiceBase
{
    public override async Task<ListCompletedQsosResponse> ListCompletedQsos(
        ListCompletedQsosRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        SessionId sessionId = ParseSessionId(request.SessionId);
        try
        {
            IReadOnlyList<Qso> qsos = await client.ListCompletedQsosAsync(
                sessionId,
                context.CancellationToken);
            var response = new ListCompletedQsosResponse();
            response.Qsos.Add(qsos.Select(TransportMapper.ToTransport));
            return response;
        }
        catch (KeyNotFoundException)
        {
            throw SessionNotFound(sessionId);
        }
    }

    public override async Task<GetResultResponse> GetResult(
        GetResultRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        SessionId sessionId = ParseSessionId(request.SessionId);
        try
        {
            return new()
            {
                Result = TransportMapper.ToTransport(
                    await client.GetResultAsync(
                        sessionId,
                        context.CancellationToken)),
            };
        }
        catch (KeyNotFoundException)
        {
            throw SessionNotFound(sessionId);
        }
    }

    public override async Task<ExportResultResponse> ExportResult(
        ExportResultRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        SessionId sessionId = ParseSessionId(request.SessionId);
        SessionResult result;
        IReadOnlyList<Qso> qsos;
        try
        {
            result = await client.GetResultAsync(
                sessionId,
                context.CancellationToken);
            qsos = await client.ListCompletedQsosAsync(
                sessionId,
                context.CancellationToken);
        }
        catch (KeyNotFoundException)
        {
            throw SessionNotFound(sessionId);
        }

        ResultExportArtifact artifact = request.Format switch
        {
            MorseRunner.Contracts.V1.ResultExportFormat.Json =>
                ResultExporter.Create(
                    result,
                    qsos,
                    MorseRunner.Infrastructure.ResultExportFormat.Json),
            MorseRunner.Contracts.V1.ResultExportFormat.Cabrillo =>
                ResultExporter.Create(
                    result,
                    qsos,
                    MorseRunner.Infrastructure.ResultExportFormat.Cabrillo),
            _ => throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "A supported result export format is required.")),
        };
        return new()
        {
            MediaType = artifact.MediaType,
            Content = ByteString.CopyFrom(artifact.Content),
            SuggestedFileName = artifact.SuggestedFileName,
        };
    }

    private static SessionId ParseSessionId(string value)
    {
        try
        {
            return TransportMapper.ParseSessionId(value);
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    private static RpcException SessionNotFound(SessionId sessionId) =>
        new(
            new Status(
                StatusCode.NotFound,
                $"Session '{sessionId}' was not found."),
            new Metadata
            {
                { "morse-error-code", DomainErrorCodes.SessionNotFound },
            });
}
