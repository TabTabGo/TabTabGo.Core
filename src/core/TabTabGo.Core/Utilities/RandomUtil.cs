namespace TabTabGo.Core.Utilities;

public static class RandomUtil
{
    private const int DefCount = 20;
    private static readonly Random Random = new Random();
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string Numbers = "0123456789";
    
    /// <summary>
    /// Generate random password
    /// </summary>
    /// <returns></returns>
    public static string GeneratePassword()
    {
        return new string(Enumerable.Repeat(Chars, DefCount)
            .Select(s => s[Random.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// Generate random numeric string
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    public static string RandomNumeric(int length)
    {
        return new string(Enumerable.Repeat(Numbers, length)
            .Select(s => s[Random.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// Generate activation key
    /// </summary>
    /// <returns></returns>
    public static string GenerateActivationKey()
    {
        return RandomNumeric(DefCount);
    }

    /// <summary>
    /// Generate reset key
    /// </summary>
    /// <returns></returns>
    public static string GenerateResetKey()
    {
        return RandomNumeric(DefCount);
    }
    
    /// <summary>
    /// Generate user stamp
    /// </summary>
    /// <returns></returns>
    public static string GenerateUserStamp()
    {
        return new string(Enumerable.Repeat(Chars, DefCount)
            .Select(s => s[Random.Next(s.Length)]).ToArray());
    }
}
