namespace TabTabGo.Core;

/// <summary>
/// A user of the system.
/// </summary>
/// <typeparam name="TKey">User key type</typeparam>
public interface IUser<TKey> where TKey : struct
{
    /// <summary>User name.</summary>
    public string Username { get; set; }

    /// <summary>User id.</summary>
    public TKey UserId { get; set; }
}
