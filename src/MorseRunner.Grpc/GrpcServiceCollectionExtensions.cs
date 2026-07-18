using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MorseRunner.Client;

namespace MorseRunner.Grpc;

public static class GrpcServiceCollectionExtensions
{
    public static IServiceCollection AddMorseRunnerGrpc(
        this IServiceCollection services,
        IMorseRunnerClient client,
        GrpcServerOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        if (String.IsNullOrWhiteSpace(options.AuthenticationToken))
        {
            throw new ArgumentException(
                "A non-empty authentication token is required.",
                nameof(options));
        }

        services.AddSingleton(client);
        services.AddSingleton(options);
        services.AddSingleton(new GrpcRequestGuard(options));
        services.AddSingleton(
            new ControlLeaseManager(options, timeProvider));
        services.AddHostedService<ControlLeaseExpiryService>();
        services.AddGrpc(grpc =>
        {
            grpc.MaxReceiveMessageSize = options.MaximumReceiveMessageSize;
            grpc.MaxSendMessageSize = options.MaximumSendMessageSize;
        });
        services.AddSingleton<EngineGrpcService>();
        services.AddSingleton<CatalogGrpcService>();
        services.AddSingleton<SessionGrpcService>();
        services.AddSingleton<ResultsGrpcService>();
        return services;
    }

    public static WebApplication MapMorseRunnerGrpc(
        this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.MapGrpcService<EngineGrpcService>();
        application.MapGrpcService<CatalogGrpcService>();
        application.MapGrpcService<SessionGrpcService>();
        application.MapGrpcService<ResultsGrpcService>();
        return application;
    }
}
