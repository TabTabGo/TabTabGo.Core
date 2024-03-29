﻿using Microsoft.Extensions.Localization;
using Nma.Lms.Test.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TabTabGo.Core.Entities;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.Services;

namespace TabTabGo.Core.Country.Services
{
    public class CountryService : ICountryService
    {
        private readonly IStringLocalizer _stringLocalizer;

        public CountryService(IStringLocalizer stringLocalizer)
        {
            _stringLocalizer = stringLocalizer;
        }

        public async Task<Models.Country?> GetCountry(string code, string culture = "en" , CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }
            var country = (await ReadData.GetCountries(cancellationToken)).FirstOrDefault(c => c.Alpha2 == code.ToUpper() || c.Alpha3 == code.ToUpper());

            if(country != null && !culture.Equals("en"))
                country.Name = _stringLocalizer.GetString(code.ToUpper(), culture);

            return country;
        }
    }
}
