using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal static class ParityObservedValuesDigest
{
    public const string Canonicalization =
        "utf8-compact-json-string-array-unescaped-nonascii-v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Compute(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        byte[] canonicalJson =
            JsonSerializer.SerializeToUtf8Bytes(values, SerializerOptions);
        return Convert.ToHexStringLower(
            SHA256.HashData(canonicalJson));
    }
}
