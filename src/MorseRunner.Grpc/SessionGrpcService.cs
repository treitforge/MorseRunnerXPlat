using Grpc.Core;
using MorseRunner.Client;
using MorseRunner.Contracts.V1;
using MorseRunner.Domain;

namespace MorseRunner.Grpc;

public sealed class SessionGrpcService(
    IMorseRunnerClient client,
    GrpcRequestGuard guard,
    ControlLeaseManager leases) : SessionService.SessionServiceBase
{
    public override async Task<CreateSessionResponse> CreateSession(
        CreateSessionRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        RequireGuid(request.RequestId, nameof(request.RequestId));
        RequireClientId(request.ClientId);
        try
        {
            SessionHandle handle = await client.CreateSessionAsync(
                TransportMapper.ToDomain(request.Settings),
                context.CancellationToken);
            return new()
            {
                Session = TransportMapper.ToTransport(handle),
            };
        }
        catch (ArgumentException exception)
        {
            throw InvalidArgument(exception.Message);
        }
    }

    public override async Task<GetSessionResponse> GetSession(
        GetSessionRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        SessionId sessionId = ParseSessionId(request.SessionId);
        try
        {
            SessionSnapshot snapshot = await client.GetSnapshotAsync(
                sessionId,
                context.CancellationToken);
            return new()
            {
                Snapshot = TransportMapper.ToTransport(
                    snapshot,
                    leases.GetSummary(sessionId)),
            };
        }
        catch (KeyNotFoundException)
        {
            throw SessionNotFound(sessionId);
        }
    }

    public override async Task<ExecuteCommandResponse> ExecuteCommand(
        ExecuteCommandRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        SessionCommand command;
        try
        {
            command = TransportMapper.ToDomain(request.Command);
        }
        catch (ArgumentException exception)
        {
            throw InvalidArgument(exception.Message);
        }

        if (!leases.Validate(
                command.SessionId,
                command.ClientId,
                request.ControlLeaseToken))
        {
            return new()
            {
                Result = TransportMapper.ToTransport(
                    new CommandResult(
                        Accepted: false,
                        DomainErrorCodes.InvalidControlLease,
                        "A current control lease is required.",
                        AppliedRevision: 0,
                        AppliedBlock: 0)),
            };
        }

        return new()
        {
            Result = TransportMapper.ToTransport(
                await client.ExecuteAsync(
                    command,
                    context.CancellationToken)),
        };
    }

    public override async Task SubscribeSession(
        SubscribeSessionRequest request,
        IServerStreamWriter<SubscribeSessionResponse> responseStream,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        SessionId sessionId = ParseSessionId(request.SessionId);
        try
        {
            await foreach (SessionUpdate update in client.SubscribeAsync(
                               new(sessionId, request.AfterSequence),
                               context.CancellationToken))
            {
                await responseStream.WriteAsync(
                    new SubscribeSessionResponse
                    {
                        Update = TransportMapper.ToTransport(
                            update,
                            leases.GetSummary(sessionId)),
                    },
                    context.CancellationToken);
            }
        }
        catch (KeyNotFoundException)
        {
            throw SessionNotFound(sessionId);
        }
    }

    public override async Task<CloseSessionResponse> CloseSession(
        CloseSessionRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        RequireGuid(request.RequestId, nameof(request.RequestId));
        SessionId sessionId = ParseSessionId(request.SessionId);
        var clientId = new ClientId(RequireClientId(request.ClientId));
        if (!leases.Validate(sessionId, clientId, request.ControlLeaseToken))
        {
            throw new RpcException(
                new Status(
                    StatusCode.PermissionDenied,
                    "A current control lease is required."));
        }

        await client.CloseSessionAsync(sessionId, context.CancellationToken);
        leases.Remove(sessionId);
        return new();
    }

    public override async Task<AcquireControlResponse> AcquireControl(
        AcquireControlRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        RequireGuid(request.RequestId, nameof(request.RequestId));
        SessionId sessionId = ParseSessionId(request.SessionId);
        await RequireSessionAsync(sessionId, context.CancellationToken);
        return new()
        {
            Lease = TransportMapper.ToTransport(
                leases.Acquire(
                    sessionId,
                    new ClientId(RequireClientId(request.ClientId)))),
        };
    }

    public override async Task<RenewControlResponse> RenewControl(
        RenewControlRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        RequireGuid(request.RequestId, nameof(request.RequestId));
        SessionId sessionId = ParseSessionId(request.SessionId);
        await RequireSessionAsync(sessionId, context.CancellationToken);
        return new()
        {
            Lease = TransportMapper.ToTransport(
                leases.Renew(
                    sessionId,
                    new ClientId(RequireClientId(request.ClientId)),
                    request.Token)),
        };
    }

    public override async Task<ReleaseControlResponse> ReleaseControl(
        ReleaseControlRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        RequireGuid(request.RequestId, nameof(request.RequestId));
        SessionId sessionId = ParseSessionId(request.SessionId);
        await RequireSessionAsync(sessionId, context.CancellationToken);
        leases.Release(
            sessionId,
            new ClientId(RequireClientId(request.ClientId)),
            request.Token);
        return new();
    }

    public override async Task<TakeControlResponse> TakeControl(
        TakeControlRequest request,
        ServerCallContext context)
    {
        guard.RequireAuthenticated(context);
        RequireGuid(request.RequestId, nameof(request.RequestId));
        SessionId sessionId = ParseSessionId(request.SessionId);
        await RequireSessionAsync(sessionId, context.CancellationToken);
        return new()
        {
            Lease = TransportMapper.ToTransport(
                leases.Take(
                    sessionId,
                    new ClientId(RequireClientId(request.ClientId)),
                    request.Force)),
        };
    }

    private async Task RequireSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.GetSnapshotAsync(sessionId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            throw SessionNotFound(sessionId);
        }
    }

    private static SessionId ParseSessionId(string value)
    {
        try
        {
            return TransportMapper.ParseSessionId(value);
        }
        catch (ArgumentException exception)
        {
            throw InvalidArgument(exception.Message);
        }
    }

    private static void RequireGuid(string value, string fieldName)
    {
        if (!Guid.TryParse(value, out _))
        {
            throw InvalidArgument($"Field '{fieldName}' must be a GUID.");
        }
    }

    private static string RequireClientId(string value)
    {
        if (String.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw InvalidArgument(
                "A client ID between 1 and 128 characters is required.");
        }

        return value;
    }

    private static RpcException InvalidArgument(string message) =>
        new(new Status(StatusCode.InvalidArgument, message));

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
