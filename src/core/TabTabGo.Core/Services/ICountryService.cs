using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TabTabGo.Core.Models;

namespace TabTabGo.Core.Services
{
    public interface ICountryService
    {
        Task<Country?> GetCountryName(string code, string culture = "en", CancellationToken cancellationToken = default);
    }
}
