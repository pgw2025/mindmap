using System.Security.Cryptography;

namespace MindMap.Api.Common.Helpers;

/// <summary>
/// PBKDF2-SHA256 密码哈希工具。盐 + 迭代次数 + 输出长度均为安全默认值。
/// 分别返回 Base64(hash) 与 Base64(salt)。
/// </summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 60_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static (string hash, string salt) Create(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, Algorithm, HashBytes);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
            return false;

        Span<byte> saltBytes;
        try
        {
            saltBytes = Convert.FromBase64String(storedSalt);
        }
        catch
        {
            return false;
        }

        var expected = Convert.FromBase64String(storedHash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, Algorithm, HashBytes);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
