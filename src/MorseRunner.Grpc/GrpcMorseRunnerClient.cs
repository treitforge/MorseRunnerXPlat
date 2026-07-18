using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using MorseRunner.Client;
using MorseRunner.Contracts.V1;
using MorseRunner.Domain;

namespace MorseRunner.Grpc;

public sealed class GrpcMorseRunnerClient : IMorseRunnerClient
{
    private readonly GrpcChannel _channel;
    private readonly Metadata _headers;
    private readonly ClientId _clientId;
    private readonly TimeProvider _timeProvider;
    private readonly EngineService.EngineServiceClient _engine;
    private readonly SessionService.SessionServiceClient _sessions;
    private readonly ResultsService.ResultsServiceClient _results;
    private readonly ConcurrentDictionary<SessionId, ControlLease> _leases = [];
    private int _disposed;

    public GrpcMorseRunnerClient(
        Uri address,
        string authenticationToken,
        ClientId clientId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId.Value);
        _clientId = clientId;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _headers = new()
        {
            { "authorization", $"Bearer {authenticationToken}" },
        };
        _channel = GrpcChannel.ForAddress(
            address,
            new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 4 * 1024 * 1024,
                MaxSendMessageSize = 1024 * 1024,
            });
        _engine = new(_channel);
        _sessions = new(_channel);
        _results = new(_channel);
    }

    public async Task<EngineInfo> GetEngineInfoAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            GetEngineInfoResponse response = await _engine.GetEngineInfoAsync(
                new(),
                _headers,
                cancellationToken: cancellationToken);
            return TransportMapper.ToDomain(response.EngineInfo);
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public Task<IReadOnlyList<AudioOutputDevice>> GetAudioOutputDevicesAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AudioOutputDevice> devices = [];
        return Task.FromResult(devices);
    }

    public async Task<SessionHandle> CreateSessionAsync(
        SessionSettings settings,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            CreateSessionResponse response = await _sessions.CreateSessionAsync(
                new()
                {
                    RequestId = RequestId.New().ToString(),
                    ClientId = _clientId.Value,
                    Settings = TransportMapper.ToTransport(settings),
                },
                _headers,
                cancellationToken: cancellationToken);
            SessionHandle handle = TransportMapper.ToDomain(response.Session);
            await AcquireControlAsync(handle.SessionId, cancellationToken);
            return handle;
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async Task<CommandResult> ExecuteAsync(
        SessionCommand command,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(command);
        if (command.ClientId != _clientId)
        {
            return new(
                Accepted: false,
                DomainErrorCodes.InvalidControlLease,
                "The command client ID does not match this client.",
                AppliedRevision: 0,
                AppliedBlock: 0);
        }

        try
        {
            ControlLease lease = await GetCurrentLeaseAsync(
                command.SessionId,
                cancellationToken);
            ExecuteCommandResponse response =
                await _sessions.ExecuteCommandAsync(
                new()
                {
                    Command = TransportMapper.ToTransport(command),
                    ControlLeaseToken = lease.Token,
                },
                _headers,
                cancellationToken: cancellationToken);
            return TransportMapper.ToDomain(response.Result);
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async Task<SessionSnapshot> GetSnapshotAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            GetSessionResponse response = await _sessions.GetSessionAsync(
                new() { SessionId = sessionId.ToString() },
                _headers,
                cancellationToken: cancellationToken);
            return TransportMapper.ToDomain(response.Snapshot);
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async IAsyncEnumerable<SessionUpdate> SubscribeAsync(
        SessionSubscription subscription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        long afterSequence = subscription.AfterSequence;
        bool reconnectAvailable = true;
        while (true)
        {
            using AsyncServerStreamingCall<SubscribeSessionResponse> call =
                _sessions.SubscribeSession(
                    new()
                    {
                        SessionId = subscription.SessionId.ToString(),
                        AfterSequence = afterSequence,
                    },
                    _headers,
                    cancellationToken: cancellationToken);
            bool reconnect = false;
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await call.ResponseStream.MoveNext(
                        cancellationToken);
                }
                catch (RpcException exception)
                    when (exception.StatusCode == StatusCode.Unavailable
                        && reconnectAvailable
                        && !cancellationToken.IsCancellationRequested)
                {
                    reconnect = true;
                    break;
                }
                catch (RpcException exception)
                {
                    throw ToTransportException(exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                SessionUpdate update = TransportMapper.ToDomain(
                    call.ResponseStream.Current.Update);
                if (update.Event is not null)
                {
                    afterSequence = update.Event.Sequence;
                }

                yield return update;
            }

            if (reconnect)
            {
                reconnectAvailable = false;
                yield return SessionUpdate.FromSnapshot(
                    await GetSnapshotAsync(
                        subscription.SessionId,
                        cancellationToken));
            }
        }
    }

    public async Task<IReadOnlyList<Qso>> ListCompletedQsosAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            ListCompletedQsosResponse response =
                await _results.ListCompletedQsosAsync(
                    new() { SessionId = sessionId.ToString() },
                    _headers,
                    cancellationToken: cancellationToken);
            return response.Qsos.Select(TransportMapper.ToDomain).ToArray();
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async Task<SessionResult> GetResultAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            GetResultResponse response = await _results.GetResultAsync(
                new() { SessionId = sessionId.ToString() },
                _headers,
                cancellationToken: cancellationToken);
            return TransportMapper.ToDomain(response.Result);
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async Task CloseSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            ControlLease lease = await GetCurrentLeaseAsync(
                sessionId,
                cancellationToken);
            _ = await _sessions.CloseSessionAsync(
                new()
                {
                    RequestId = RequestId.New().ToString(),
                    SessionId = sessionId.ToString(),
                    ClientId = _clientId.Value,
                    ControlLeaseToken = lease.Token,
                },
                _headers,
                cancellationToken: cancellationToken);
            _leases.TryRemove(sessionId, out _);
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async Task<ControlLease> AcquireControlAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            AcquireControlResponse response =
                await _sessions.AcquireControlAsync(
                new()
                {
                    RequestId = RequestId.New().ToString(),
                    SessionId = sessionId.ToString(),
                    ClientId = _clientId.Value,
                },
                _headers,
                cancellationToken: cancellationToken);
            ControlLease lease = TransportMapper.ToDomain(response.Lease);
            _leases[sessionId] = lease;
            return lease;
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async Task ReleaseControlAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_leases.TryGetValue(sessionId, out ControlLease? lease))
        {
            return;
        }

        try
        {
            _ = await _sessions.ReleaseControlAsync(
                new()
                {
                    RequestId = RequestId.New().ToString(),
                    SessionId = sessionId.ToString(),
                    ClientId = _clientId.Value,
                    Token = lease.Token,
                },
                _headers,
                cancellationToken: cancellationToken);
            _leases.TryRemove(sessionId, out _);
        }
        catch (RpcException exception)
        {
            throw ToTransportException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _leases.Clear();
        _channel.Dispose();
        await ValueTask.CompletedTask;
    }

    private async Task<ControlLease> GetCurrentLeaseAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (!_leases.TryGetValue(sessionId, out ControlLease? lease))
        {
            return await AcquireControlAsync(sessionId, cancellationToken);
        }

        if (lease.ExpiresAt - _timeProvider.GetUtcNow() > TimeSpan.FromSeconds(2))
        {
            return lease;
        }

        RenewControlResponse response = await _sessions.RenewControlAsync(
            new()
            {
                RequestId = RequestId.New().ToString(),
                SessionId = sessionId.ToString(),
                ClientId = _clientId.Value,
                Token = lease.Token,
            },
            _headers,
            cancellationToken: cancellationToken);
        ControlLease renewed = TransportMapper.ToDomain(response.Lease);
        _leases[sessionId] = renewed;
        return renewed;
    }

    private static MorseRunnerTransportException ToTransportException(
        RpcException exception)
    {
        string? domainCode = exception.Trailers.FirstOrDefault(
            entry => entry.Key == "morse-error-code")?.Value;
        return new(
            exception.StatusCode.ToString(),
            domainCode is null
                ? exception.Status.Detail
                : $"{domainCode}: {exception.Status.Detail}",
            exception);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }
}
