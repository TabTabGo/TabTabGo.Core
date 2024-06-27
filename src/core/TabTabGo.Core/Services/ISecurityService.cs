namespace TabTabGo.Core.Services;

public interface ISecurityService<TUserKey,TTenantKey> where TUserKey : struct where TTenantKey : struct
{
    User<TUserKey>? GetUser();
    TUserKey? GetUserId();
    Tenant<TTenantKey>? GetTenant();
    TTenantKey? GetInstanceId();
    bool IsAuthenticated();
    bool IsAuthorized(string roles);
}
