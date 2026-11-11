using System.Security.Claims;

namespace TabTabGo.Core.Services;

/// <summary>
/// Reads user and tenant information from a claims principal.
/// </summary>
/// <typeparam name="TUserKey">User key type</typeparam>
/// <typeparam name="TTenantKey">Tenant key type</typeparam>
public interface IParseClaimsPrincipal<TUserKey, out TTenantKey> where TUserKey : struct where TTenantKey : struct
{
    /// <summary>User id.</summary>
    TUserKey GetUserId(ClaimsPrincipal principal);

    /// <summary>User name.</summary>
    string GetUsername(ClaimsPrincipal principal);

    /// <summary>The user.</summary>
    IUser<TUserKey> GetUser(ClaimsPrincipal principal);

    /// <summary>Roles of the user.</summary>
    string[] GetUserRoles(ClaimsPrincipal principal);

    /// <summary>Tenant id.</summary>
    TTenantKey GetTenantId(ClaimsPrincipal principal);

    /// <summary>Tenant code.</summary>
    string GetTenantCode(ClaimsPrincipal principal);
}
