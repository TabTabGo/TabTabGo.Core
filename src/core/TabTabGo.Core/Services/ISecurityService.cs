namespace TabTabGo.Core.Services;

public interface ISecurityService
{
    User? GetUser();
    int? GetUserId();
    Instance? GetInstance();
    int? GetInstanceId();
    bool IsAuthenticated();
    bool IsAuthorized(string roles);
}