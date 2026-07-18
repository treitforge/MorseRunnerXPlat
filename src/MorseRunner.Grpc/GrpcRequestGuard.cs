using System.Security.Cryptography;
using System.Text;
using Grpc.Core;

namespace MorseRunner.Grpc;

public sealed class GrpcRequestGuard(GrpcServerOptions options)
{
    private readonly byte[] _expectedToken = Encoding.UTF8.GetBytes(
        options.AuthenticationToken);

    public void RequireAuthenticated(ServerCallContext context)
    {
        string? authorization = context.RequestHeaders
            .FirstOrDefault(
                entry => String.Equals(
                    entry.Key,
                    "authorization",
                    StringComparison.OrdinalIgnoreCase))
            ?.Value;
        const string prefix = "Bearer ";
        if (authorization is null
            || !authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw Unauthenticated();
        }

        byte[] supplied = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
        bool matches = supplied.Length == _expectedToken.Length
            && CryptographicOperations.FixedTimeEquals(supplied, _expectedToken);
        CryptographicOperations.ZeroMemory(supplied);
        if (!matches)
        {
            throw Unauthenticated();
        }
    }

    private static RpcException Unauthenticated() =>
        new(new Status(
            StatusCode.Unauthenticated,
            "A valid per-launch host token is required."));
}
