using Microsoft.Extensions.DependencyInjection;
using TabTabGo.Core.Country.Services;
using Microsoft.Extensions.Localization;
using TabTabGo.Core.Country;

namespace TabTabGo.Core.Test
{
    public class SetupServices
    {
        private readonly IServiceCollection _servicesCollection;
        private IServiceProvider _serviceProvider;

        public SetupServices()
        {
            _servicesCollection = new ServiceCollection();
            _servicesCollection = AddServices();
        }

        public TService GetRequiredService<TService>()
        {
            _serviceProvider ??= _servicesCollection.BuildServiceProvider().CreateScope().ServiceProvider;
            return _serviceProvider.GetService<TService>();
        }

        private IServiceCollection AddServices()
        {

            _servicesCollection.AddScoped<ICountryService, CountryService>();
            _servicesCollection.AddScoped<IStringLocalizer, JsonStringLocalizer>();
            _servicesCollection.AddDistributedMemoryCache();
            _serviceProvider = _servicesCollection.BuildServiceProvider();

            return _servicesCollection;
        }
    }
}
