using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TabTabGo.Core.Country.Services;
using TabTabGo.Core.Services;

namespace TabTabGo.Core.Test;

public class SetupServices
{
    private readonly IServiceProvider _serviceProvider;

    public SetupServices()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<IStringLocalizer, JsonStringLocalizer>();
        services.AddDistributedMemoryCache();
        _serviceProvider = services.BuildServiceProvider().CreateScope().ServiceProvider;
    }

    public TService GetRequiredService<TService>() where TService : notnull => _serviceProvider.GetRequiredService<TService>();
}
