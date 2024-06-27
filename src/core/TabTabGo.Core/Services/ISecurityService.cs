namespace TabTabGo.Core.Services;

public interface ISecurityService<TUserKey, TTenantKey> where TUserKey : struct where TTenantKey : struct
{
    /// <summary>
    /// Get current User entity
    /// </summary>
    /// <returns></returns>
    IUser<TUserKey>? GetUser();
    
    /// <summary>
    /// Get Current Tenant entity
    /// </summary>
    /// <returns></returns>
    ITenant<TTenantKey>? GetTenant();
    bool IsAuthorized(string roles);
    /// <summary>
    /// Reset Role Permission Cached Data
    /// </summary>
    void ResetPermissions();
    
    /// <summary>
    /// User is authenticated
    /// </summary>
    /// <returns></returns>
    bool IsAuthenticated();
    
    /// <summary>
    /// Get user id
    /// </summary>
    /// <returns></returns>
    TUserKey? GetUserId();
    
    /// <summary>
    /// If login as client then get client Id
    /// </summary>
    /// <returns></returns>
    string? GetClientId();
    /// <summary>
    /// Get username
    /// </summary>
    string? GetUsername();
    /// <summary>
    /// Get User email
    /// </summary>
    /// <returns></returns>
    string? GetEmail();
    
    /// <summary>
    /// Get user full name
    /// </summary>
    /// <returns></returns>
    string? GetFullName();
    /// <summary>
    /// is user granted with role
    /// </summary>
    /// <param name="role">role code</param>
    /// <returns></returns>
    bool IsInRole(string role);
    
    /// <summary>
    /// Is user granted with roles
    /// </summary>
    /// <param name="roles">list of roles</param>
    /// <returns></returns>
    bool IsInRoles(string[] roles);
    
    /// <summary>
    /// Is user granted any of the roles
    /// </summary>
    /// <param name="roles">list of roles</param>
    /// <returns></returns>
    bool IsInAnyRole(string[] roles);
    
    /// <summary>
    /// is user granted with a role in instance
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    bool IsInTenant(int tenantId);
 
    /// <summary>
    /// is user granted with role in department
    /// </summary>
    /// <param name="instance"></param>
    /// <param name="role"></param>
    /// <returns></returns>
    bool IsInTenantWithRole(int instance, string role);

    /// <summary>
    /// get user roles
    /// </summary>
    /// <returns></returns>
    List<string> GetRoles();
    IDictionary<string, Dictionary<string, List<int>>> GetRolePermissions();
    
    /// <summary>
    /// Get user permissions
    /// </summary>
    /// <returns></returns>
    Dictionary<string, IList<int>?> GetPermissions();

    /// <summary>
    /// Get Permissions for entity
    /// </summary>
    /// <param name="entityType"></param>
    /// <returns></returns>
    long GetPermissions(Entity entityType);
    
    /// <summary>
    /// Get current instance id
    /// </summary>
    /// <returns></returns>
    TTenantKey? GetTenantId();
    
    /// <summary>
    /// Get current instance code
    /// </summary>
    /// <returns></returns>
    string? GetTenantCode();
    /// <summary>
    /// Get dictionary of tenantIds and roles
    /// </summary>
    /// <returns></returns>
    Dictionary<TTenantKey, List<string>>? GetUserTenantsAndRoles();

}
