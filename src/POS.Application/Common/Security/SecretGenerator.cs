using System.Security.Cryptography;

namespace POS.Application.Common.Security;

/// <summary>
/// Generates the two secrets enrollment deals in. Both come from the cryptographic RNG —
/// <see cref="Random"/> is seeded predictably enough that codes could be guessed in bulk.
/// </summary>
public static class SecretGenerator
{
    /// <summary>
    /// Alphabet for codes a human retypes: no O/0, I/1/L, U/V confusion. Someone is reading
    /// this off a screen and keying it into a till, possibly badly lit and in a hurry.
    /// </summary>
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTWXYZ";

    private const string ApiKeyPrefix = "miq_";

    /// <summary>Formatted "XXXX-XXXX" — 29^8, about 5.0e11 combinations.</summary>
    public static string NewEnrollmentCode()
    {
        Span<char> buffer = stackalloc char[9];
        for (int i = 0, c = 0; i < 9; i++)
        {
            if (i == 4) { buffer[i] = '-'; continue; }
            buffer[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            c++;
        }
        return new string(buffer);
    }

    /// <summary>
    /// 256 bits of entropy, base64url so it survives being pasted into a config file or an
    /// HTTP header without escaping. Prefixed so it is recognisable in a support ticket and
    /// greppable in a secret scanner.
    /// </summary>
    public static string NewApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return ApiKeyPrefix + Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
