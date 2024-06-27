using System.Security.Claims;
using TabTabGo.Core;

namespace TabTabGo.Core.Services
{
    public interface IParseClaimsPrincipal<TUserKey,TTenantKey> where TUserKey : struct where TTenantKey : struct
    {
        TUserKey GetUserId(ClaimsPrincipal principal);
        string GetUsername(ClaimsPrincipal principal);
        User<TUserKey> GetUser(ClaimsPrincipal principal);
        string[] GetUserRoles(ClaimsPrincipal principal);
        TTenantKey GetTenantId(ClaimsPrincipal principal);
        string GetTenantCode(ClaimsPrincipal principal);
        Tenant<TTenantKey> GetTenant(ClaimsPrincipal principal);
    }
}
