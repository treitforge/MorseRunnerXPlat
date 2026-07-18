using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using MorseRunner.Audio;
using MorseRunner.Client;
using MorseRunner.Engine;
using MorseRunner.Grpc;
using MorseRunner.Infrastructure;

return await MorseRunner.EngineHost.EngineHostProgram.RunAsync(args);

namespace MorseRunner.EngineHost
{
    public static class EngineHostProgram
    {
        private static readonly Action<ILogger, string, Exception?> HostReady =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(1, nameof(HostReady)),
                "MorseRunner engine host ready at {Endpoint}.");

        public static async Task<int> RunAsync(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            int port = builder.Configuration.GetValue("port", 0);
            builder.WebHost.ConfigureKestrel(
                server => server.Listen(
                    IPAddress.Loopback,
                    port,
                    listen => listen.Protocols = HttpProtocols.Http2));

            string token = builder.Configuration["token"]
                ?? Environment.GetEnvironmentVariable("MORSE_RUNNER_HOST_TOKEN")
                ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            string? dataRoot = builder.Configuration["data-root"];
            bool usePhysicalAudio = builder.Configuration.GetValue(
                "physical-audio",
                false);

            await using var client = new InProcessMorseRunnerClient(
                usePhysicalAudio
                    ? new MorseRunnerEngine(
                        _ => new PhysicalAudioSink(new()))
                    : new MorseRunnerEngine(_ => new NullAudioSink()));
            var options = new GrpcServerOptions
            {
                AuthenticationToken = token,
                AllowForcedTakeover = false,
            };
            builder.Services.AddMorseRunnerGrpc(client, options);

            WebApplication application = builder.Build();
            application.MapMorseRunnerGrpc();

            var paths = new ApplicationPaths(dataRoot);
            var discovery = new HostDiscoveryStore(paths);
            int processId = Environment.ProcessId;
            try
            {
                await application.StartAsync();
                string endpoint = ResolveEndpoint(application);
                var info = await client.GetEngineInfoAsync(CancellationToken.None);
                await discovery.PublishAsync(
                    new(
                        endpoint,
                        processId,
                        info.EngineEpoch,
                        info.MaximumContractVersion,
                        token,
                        DateTimeOffset.UtcNow),
                    CancellationToken.None);
                HostReady(application.Logger, endpoint, null);
                await application.WaitForShutdownAsync();
                return 0;
            }
            finally
            {
                discovery.RemoveIfOwned(processId, token);
                await application.DisposeAsync();
            }
        }

        private static string ResolveEndpoint(WebApplication application)
        {
            IServer server = application.Services.GetRequiredService<IServer>();
            IServerAddressesFeature feature = server.Features
                .Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException(
                    "The server did not publish an endpoint.");
            return feature.Addresses.Single();
        }
    }
}
