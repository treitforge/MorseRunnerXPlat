using System.Collections.Concurrent;
using System.Security.Cryptography;
using Grpc.Core;
using MorseRunner.Domain;

namespace MorseRunner.Grpc;

public sealed class ControlLeaseExpiredEventArgs(
    SessionId SessionId,
    ClientId ClientId) : EventArgs
{
    public SessionId SessionId { get; } = SessionId;

    public ClientId ClientId { get; } = ClientId;
}

public sealed class ControlLeaseManager : IDisposable
{
    private readonly ConcurrentDictionary<SessionId, LeaseRecord> _leases = [];
    private readonly GrpcServerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;

    public ControlLeaseManager(
        GrpcServerOptions options,
        TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timer = _timeProvider.CreateTimer(
            static state => ((ControlLeaseManager)state!).SweepExpired(),
            this,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(250));
    }

    public event EventHandler<ControlLeaseExpiredEventArgs>? LeaseExpired;

    public ControlLease Acquire(SessionId sessionId, ClientId clientId)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        while (true)
        {
            if (_leases.TryGetValue(sessionId, out LeaseRecord? existing))
            {
                if (existing.ExpiresAt + _options.ControlLeaseGracePeriod > now)
                {
                    if (existing.ClientId == clientId)
                    {
                        return existing.ToDomain(sessionId);
                    }

                    throw LeaseError(
                        StatusCode.FailedPrecondition,
                        DomainErrorCodes.ControlLeaseHeld,
                        "Another client currently holds control.");
                }

                _leases.TryRemove(
                    new KeyValuePair<SessionId, LeaseRecord>(
                        sessionId,
                        existing));
            }

            LeaseRecord created = Create(clientId, now, revision: 1);
            if (_leases.TryAdd(sessionId, created))
            {
                return created.ToDomain(sessionId);
            }
        }
    }

    public ControlLease Renew(
        SessionId sessionId,
        ClientId clientId,
        string token)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!_leases.TryGetValue(sessionId, out LeaseRecord? current))
        {
            throw LeaseError(
                StatusCode.FailedPrecondition,
                DomainErrorCodes.ControlLeaseRequired,
                "The session has no control lease.");
        }

        ValidateOwnerAndToken(current, clientId, token);
        if (current.ExpiresAt + _options.ControlLeaseGracePeriod <= now)
        {
            _leases.TryRemove(sessionId, out _);
            throw LeaseError(
                StatusCode.FailedPrecondition,
                DomainErrorCodes.ControlLeaseExpired,
                "The control lease has expired.");
        }

        LeaseRecord renewed = current with
        {
            ExpiresAt = now + _options.ControlLeaseDuration,
            Revision = current.Revision + 1,
        };
        if (!_leases.TryUpdate(sessionId, renewed, current))
        {
            return Renew(sessionId, clientId, token);
        }

        return renewed.ToDomain(sessionId);
    }

    public void Release(
        SessionId sessionId,
        ClientId clientId,
        string token)
    {
        if (!_leases.TryGetValue(sessionId, out LeaseRecord? current))
        {
            return;
        }

        ValidateOwnerAndToken(current, clientId, token);
        _leases.TryRemove(
            new KeyValuePair<SessionId, LeaseRecord>(sessionId, current));
    }

    public ControlLease Take(
        SessionId sessionId,
        ClientId clientId,
        bool force)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_leases.TryGetValue(sessionId, out LeaseRecord? current)
            && current.ExpiresAt + _options.ControlLeaseGracePeriod > now)
        {
            if (!force)
            {
                throw LeaseError(
                    StatusCode.FailedPrecondition,
                    DomainErrorCodes.ControlLeaseHeld,
                    "The current lease has not expired.");
            }

            if (!_options.AllowForcedTakeover)
            {
                throw LeaseError(
                    StatusCode.PermissionDenied,
                    DomainErrorCodes.UnsupportedCapability,
                    "Forced control takeover is disabled.");
            }
        }

        long revision = current?.Revision + 1 ?? 1;
        LeaseRecord replacement = Create(clientId, now, revision);
        _leases[sessionId] = replacement;
        return replacement.ToDomain(sessionId);
    }

    public bool Validate(
        SessionId sessionId,
        ClientId clientId,
        string token)
    {
        if (!_leases.TryGetValue(sessionId, out LeaseRecord? lease))
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        return lease.ExpiresAt + _options.ControlLeaseGracePeriod > now
            && lease.ClientId == clientId
            && TokensEqual(lease.Token, token);
    }

    public ControlLeaseSummary? GetSummary(SessionId sessionId)
    {
        if (!_leases.TryGetValue(sessionId, out LeaseRecord? lease)
            || lease.ExpiresAt + _options.ControlLeaseGracePeriod
                <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new(lease.ClientId, lease.ExpiresAt, lease.Revision);
    }

    public void Remove(SessionId sessionId)
    {
        _leases.TryRemove(sessionId, out _);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private LeaseRecord Create(
        ClientId clientId,
        DateTimeOffset now,
        long revision)
    {
        string token = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));
        return new(
            token,
            clientId,
            now,
            now + _options.ControlLeaseDuration,
            revision);
    }

    private void SweepExpired()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((SessionId sessionId, LeaseRecord lease) in _leases)
        {
            if (lease.ExpiresAt + _options.ControlLeaseGracePeriod > now
                || !_leases.TryRemove(
                    new KeyValuePair<SessionId, LeaseRecord>(
                        sessionId,
                        lease)))
            {
                continue;
            }

            LeaseExpired?.Invoke(
                this,
                new(sessionId, lease.ClientId));
        }
    }

    private static void ValidateOwnerAndToken(
        LeaseRecord lease,
        ClientId clientId,
        string token)
    {
        if (lease.ClientId != clientId || !TokensEqual(lease.Token, token))
        {
            throw LeaseError(
                StatusCode.PermissionDenied,
                DomainErrorCodes.InvalidControlLease,
                "The control lease token is invalid.");
        }
    }

    private static bool TokensEqual(string expected, string supplied)
    {
        byte[] left = Convert.FromBase64String(expected);
        byte[] right;
        try
        {
            right = Convert.FromBase64String(supplied);
        }
        catch (FormatException)
        {
            return false;
        }

        return left.Length == right.Length
            && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static RpcException LeaseError(
        StatusCode statusCode,
        string errorCode,
        string message) =>
        new(
            new Status(statusCode, message),
            new Metadata { { "morse-error-code", errorCode } });

    private sealed record LeaseRecord(
        string Token,
        ClientId ClientId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        long Revision)
    {
        public ControlLease ToDomain(SessionId sessionId) =>
            new(Token, sessionId, ClientId, IssuedAt, ExpiresAt, Revision);
    }
}
