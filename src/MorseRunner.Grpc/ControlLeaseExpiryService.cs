using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using MorseRunner.Client;
using MorseRunner.Domain;

namespace MorseRunner.Grpc;

public sealed class ControlLeaseExpiryService(
    ControlLeaseManager leases,
    IMorseRunnerClient client) : BackgroundService
{
    private readonly Channel<ControlLeaseExpiredEventArgs> _expired =
        Channel.CreateBounded<ControlLeaseExpiredEventArgs>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        leases.LeaseExpired += OnLeaseExpired;
        try
        {
            await foreach (ControlLeaseExpiredEventArgs expired
                           in _expired.Reader.ReadAllAsync(stoppingToken))
            {
                _ = await client.ExecuteAsync(
                    new ExpireControlLeaseCommand(
                        RequestId.New(),
                        expired.SessionId,
                        expired.ClientId),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            leases.LeaseExpired -= OnLeaseExpired;
        }
    }

    private void OnLeaseExpired(
        object? sender,
        ControlLeaseExpiredEventArgs eventArgs)
    {
        _expired.Writer.TryWrite(eventArgs);
    }
}
