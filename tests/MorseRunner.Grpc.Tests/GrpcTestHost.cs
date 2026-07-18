using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MorseRunner.Audio;
using MorseRunner.Client;
using MorseRunner.Engine;
using MorseRunner.Grpc;

namespace MorseRunner.Grpc.Tests;

internal sealed class GrpcTestHost : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly InProcessMorseRunnerClient _engineClient;

    private GrpcTestHost(
        WebApplication application,
        InProcessMorseRunnerClient engineClient,
        Uri address,
        string token)
    {
        _application = application;
        _engineClient = engineClient;
        Address = address;
        Token = token;
    }

    public Uri Address { get; }

    public string Token { get; }

    public static async Task<GrpcTestHost> StartAsync(
        TimeProvider? timeProvider = null)
    {
        const string token = "test-host-token";
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(
            server => server.Listen(
                IPAddress.Loopback,
                0,
                listen => listen.Protocols = HttpProtocols.Http2));
        var engineClient = new InProcessMorseRunnerClient(
            new MorseRunnerEngine(_ => new NullAudioSink()));
        builder.Services.AddMorseRunnerGrpc(
            engineClient,
            new GrpcServerOptions
            {
                AuthenticationToken = token,
                ControlLeaseDuration = TimeSpan.FromSeconds(10),
                ControlLeaseGracePeriod = TimeSpan.FromSeconds(2),
            },
            timeProvider);
        WebApplication application = builder.Build();
        application.MapMorseRunnerGrpc();
        await application.StartAsync();

        IServer server = application.Services.GetRequiredService<IServer>();
        string address = server.Features.Get<IServerAddressesFeature>()!
            .Addresses
            .Single();
        return new(
            application,
            engineClient,
            new Uri(address),
            token);
    }

    public GrpcMorseRunnerClient CreateClient(string clientId) =>
        new(Address, Token, new(clientId));

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
        await _engineClient.DisposeAsync();
    }
}
