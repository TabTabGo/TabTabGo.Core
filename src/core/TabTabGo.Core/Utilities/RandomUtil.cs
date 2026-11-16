using System.Security.Cryptography;

namespace TabTabGo.Core.Utilities;

/// <summary>
/// Cryptographically secure random strings for passwords, activation keys, reset keys and user stamps.
/// </summary>
public static class RandomUtil
{
    private const int DefCount = 20;
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string Numbers = "0123456789";

    /// <summary>
    /// Generate random password
    /// </summary>
    public static string GeneratePassword()
    {
        return RandomNumberGenerator.GetString(Chars, DefCount);
    }

    /// <summary>
    /// Generate random numeric string
    /// </summary>
    /// <param name="length">Number of digits</param>
    public static string RandomNumeric(int length)
    {
        return RandomNumberGenerator.GetString(Numbers, length);
    }

    /// <summary>
    /// Generate activation key
    /// </summary>
    public static string GenerateActivationKey()
    {
        return RandomNumeric(DefCount);
    }

    /// <summary>
    /// Generate reset key
    /// </summary>
    public static string GenerateResetKey()
    {
        return RandomNumeric(DefCount);
    }

    /// <summary>
    /// Generate user stamp
    /// </summary>
    public static string GenerateUserStamp()
    {
        return RandomNumberGenerator.GetString(Chars, DefCount);
    }
}
