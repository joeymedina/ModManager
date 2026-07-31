using Microsoft.Extensions.DependencyInjection;

namespace ModManager.Infrastructure.Extensions
{
    public static class InfrastructureServiceRegistrations
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddInfrastructureServices()
            {
                return services;
            }
        }
    }
}
