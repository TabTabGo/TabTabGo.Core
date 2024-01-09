using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Core.Country.Services
{
    public interface ICountryService
    {
        Task<string> GetCountryName(string code, string culture = "en");
    }
}
