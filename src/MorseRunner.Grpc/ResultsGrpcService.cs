using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using MorseRunner.Client;
using MorseRunner.Contracts.V1;
using MorseRunner.Domain;

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

        return request.Format switch
        {
            ResultExportFormat.Json => new()
            {
                MediaType = "application/json",
                Content = ByteString.CopyFrom(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new { result, qsos })),
                SuggestedFileName = $"{sessionId}.json",
            },
            ResultExportFormat.Cabrillo => new()
            {
                MediaType = "text/plain; charset=utf-8",
                Content = ByteString.CopyFromUtf8(
                    ExportCabrillo(result, qsos)),
                SuggestedFileName = $"{sessionId}.log",
            },
            _ => throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "A supported result export format is required.")),
        };
    }

    private static string ExportCabrillo(
        SessionResult result,
        IReadOnlyList<Qso> qsos)
    {
        var builder = new StringBuilder();
        builder.AppendLine("START-OF-LOG: 3.0");
        builder.Append("CONTEST: ").AppendLine(result.ContestId.Value);
        builder.Append("CLAIMED-SCORE: ").AppendLine(
            result.Score.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (Qso qso in qsos)
        {
            builder.Append("QSO: ")
                .Append(qso.Call)
                .Append(' ')
                .Append(qso.Rst)
                .Append(' ')
                .Append(qso.Exchange1)
                .Append(' ')
                .AppendLine(qso.Exchange2);
        }

        builder.AppendLine("END-OF-LOG:");
        return builder.ToString();
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
