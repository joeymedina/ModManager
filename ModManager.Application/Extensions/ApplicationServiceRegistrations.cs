using Microsoft.Extensions.DependencyInjection;

namespace ModManager.Application.Extensions
{
    public static class ApplicationServiceRegistrations
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddApplicationServices()
            {
                return services;
            }
        }
    }
}
