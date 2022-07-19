using System.Security.Claims;
using TabTabGo.Core;

namespace TabTabGo.Core.Services
{
    public interface IParseClaimsPrincipal
    {
        int GetUserId(ClaimsPrincipal principal);
        string GetUsername(ClaimsPrincipal principal);
        User GetUser(ClaimsPrincipal principal);
        string[] GetUserRoles(ClaimsPrincipal principal);
        int GetInstanceId(ClaimsPrincipal principal);
        string GetInstanceCode(ClaimsPrincipal principal);
        Instance GetInstance(ClaimsPrincipal principal);
    }
}
