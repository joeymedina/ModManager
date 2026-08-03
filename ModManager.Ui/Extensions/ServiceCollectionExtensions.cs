using Microsoft.Extensions.DependencyInjection;
using ModManager.Application.Interfaces;
using ModManager.Application.Services;
using ModManager.Ui.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace ModManager.Ui.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddUiServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddTransient<MainViewModel>();
            return services;
        }
    }
}
