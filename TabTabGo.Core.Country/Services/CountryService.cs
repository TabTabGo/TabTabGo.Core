using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Core.Country.Services
{
    public class CountryService : ICountryService
    {
        private readonly IStringLocalizer _localization;

        public CountryService(IStringLocalizer localization)
        {
            _localization = localization;
        }

        public async Task<string> GetCountryName(string code, string culture = "en")
        {
            if (string.IsNullOrEmpty(code))
            {
                return code;
            }

            RegionInfo region = new RegionInfo(code);
            string countryName = region.EnglishName;
            if(culture != "en")
                countryName = _localization.GetString(countryName, culture);
            return countryName;
        }
    }
}
