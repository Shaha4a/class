using ClassIn.Application.Contracts;
using ClassIn.Infrastructure.Data;
using ClassIn.Infrastructure.Services;

namespace ClassIn.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<ISqlConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<IMessageService, MessageService>();

        return services;
    }
}

