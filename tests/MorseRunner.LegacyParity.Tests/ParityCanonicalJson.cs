using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MorseRunner.LegacyParity.Tests;

internal static class ParityCanonicalJson
{
    private const string HexDigits = "0123456789abcdef";

    public static string Serialize(JsonElement value)
    {
        return Encoding.UTF8.GetString(SerializeToUtf8Bytes(value));
    }

    public static byte[] SerializeToUtf8Bytes(JsonElement value)
    {
        using MemoryStream stream = new();
        Write(stream, value);

        return stream.ToArray();
    }

    public static string ComputeSha256(JsonElement value)
    {
        return ComputeSha256(SerializeToUtf8Bytes(value));
    }

    public static string ComputeProjectionSha256(
        JsonElement value,
        IReadOnlyList<string> propertyNames)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Canonical JSON projection source is not an object.");
        }

        ValidateNoDuplicateProperties(value);
        string[] orderedNames = propertyNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (orderedNames.Length == 0
            || orderedNames.Distinct(StringComparer.Ordinal).Count()
                != orderedNames.Length)
        {
            throw new InvalidDataException(
                "Canonical JSON projection fields are invalid.");
        }

        using MemoryStream stream = new();
        WriteAscii(stream, "{");
        for (int index = 0; index < orderedNames.Length; index++)
        {
            string propertyName = orderedNames[index];
            ValidateUnicodeScalarString(
                propertyName,
                "Canonical JSON projection property");
            if (index > 0)
            {
                WriteAscii(stream, ",");
            }

            WriteString(stream, propertyName);
            WriteAscii(stream, ":");
            Write(stream, value.GetProperty(propertyName));
        }

        WriteAscii(stream, "}");
        return ComputeSha256(stream.ToArray());
    }

    public static bool IsLowercaseSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(
                character =>
                    character is >= '0' and <= '9'
                    or >= 'a' and <= 'f');
    }

    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(
                character =>
                    character is >= '0' and <= '9'
                    or >= 'a' and <= 'f'
                    or >= 'A' and <= 'F');
    }

    public static string ComputeSha256(ReadOnlySpan<byte> value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static void Write(
        Stream stream,
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(stream, value);
                break;
            case JsonValueKind.Array:
                WriteAscii(stream, "[");
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        WriteAscii(stream, ",");
                    }

                    Write(stream, item);
                }

                WriteAscii(stream, "]");
                break;
            case JsonValueKind.String:
                string text;
                try
                {
                    text = value.GetString()
                        ?? throw new InvalidDataException(
                            "Parity JSON string is invalid.");
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidDataException(
                        "Parity JSON string contains invalid Unicode.",
                        exception);
                }

                ValidateUnicodeScalarString(
                    text,
                    "Parity JSON string");
                WriteString(stream, text);
                break;
            case JsonValueKind.Number:
                WriteSignedInt64(stream, value);
                break;
            case JsonValueKind.True:
                WriteAscii(stream, "true");
                break;
            case JsonValueKind.False:
                WriteAscii(stream, "false");
                break;
            case JsonValueKind.Null:
                WriteAscii(stream, "null");
                break;
            default:
                throw new InvalidDataException(
                    "Parity JSON contains an unsupported value.");
        }
    }

    private static void ValidateNoDuplicateProperties(
        JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            JsonProperty[] properties =
                value.EnumerateObject().ToArray();
            string[] propertyNames = properties
                .Select(GetPropertyName)
                .ToArray();
            if (propertyNames
                .GroupBy(
                    propertyName => propertyName,
                    StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
            {
                throw new InvalidDataException(
                    "Parity JSON contains a duplicate object property.");
            }

            for (int index = 0; index < properties.Length; index++)
            {
                ValidateUnicodeScalarString(
                    propertyNames[index],
                    "Parity JSON object property");
                ValidateNoDuplicateProperties(
                    properties[index].Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateNoDuplicateProperties(item);
            }
        }
    }

    private static void WriteObject(
        Stream stream,
        JsonElement value)
    {
        CanonicalProperty[] properties = value
            .EnumerateObject()
            .Select(
                property => new CanonicalProperty(
                    GetPropertyName(property),
                    property.Value))
            .OrderBy(
                property => property.Name,
                StringComparer.Ordinal)
            .ToArray();
        if (properties
            .GroupBy(
                property => property.Name,
                StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException(
                "Parity JSON contains a duplicate object property.");
        }

        WriteAscii(stream, "{");
        for (int index = 0; index < properties.Length; index++)
        {
            CanonicalProperty property = properties[index];
            ValidateUnicodeScalarString(
                property.Name,
                "Parity JSON object property");
            if (index > 0)
            {
                WriteAscii(stream, ",");
            }

            WriteString(stream, property.Name);
            WriteAscii(stream, ":");
            Write(stream, property.Value);
        }

        WriteAscii(stream, "}");
    }

    private static void WriteSignedInt64(
        Stream stream,
        JsonElement value)
    {
        string raw = value.GetRawText();
        int firstDigit = raw.Length > 0 && raw[0] == '-'
            ? 1
            : 0;
        if (firstDigit == raw.Length
            || raw.AsSpan(firstDigit).ContainsAnyExceptInRange(
                '0',
                '9')
            || !Int64.TryParse(
                raw,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out long number))
        {
            throw new InvalidDataException(
                "Parity JSON numbers must be signed Int64 literals. "
                + "Use strings for fractional values.");
        }

        WriteAscii(
            stream,
            number.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteString(Stream stream, string value)
    {
        ValidateUnicodeScalarString(value, "Parity JSON string");
        WriteAscii(stream, "\"");
        Span<byte> utf8 = stackalloc byte[4];
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"':
                    WriteAscii(stream, "\\\"");
                    continue;
                case '\\':
                    WriteAscii(stream, "\\\\");
                    continue;
                case '\b':
                    WriteAscii(stream, "\\b");
                    continue;
                case '\f':
                    WriteAscii(stream, "\\f");
                    continue;
                case '\n':
                    WriteAscii(stream, "\\n");
                    continue;
                case '\r':
                    WriteAscii(stream, "\\r");
                    continue;
                case '\t':
                    WriteAscii(stream, "\\t");
                    continue;
            }

            if (character < ' ')
            {
                WriteAscii(stream, "\\u00");
                stream.WriteByte(
                    (byte)HexDigits[(character >> 4) & 0x0f]);
                stream.WriteByte(
                    (byte)HexDigits[character & 0x0f]);
                continue;
            }

            Rune rune;
            if (Char.IsHighSurrogate(character))
            {
                rune = new Rune(character, value[++index]);
            }
            else
            {
                rune = new Rune(character);
            }

            int byteCount = rune.EncodeToUtf8(utf8);
            stream.Write(utf8[..byteCount]);
        }

        WriteAscii(stream, "\"");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        foreach (char character in value)
        {
            if (character > 0x7f)
            {
                throw new InvalidOperationException(
                    "Canonical JSON internal ASCII token is invalid.");
            }

            stream.WriteByte((byte)character);
        }
    }

    private static string GetPropertyName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException(
                "Parity JSON object property contains invalid Unicode.",
                exception);
        }
    }

    private static void ValidateUnicodeScalarString(
        string value,
        string label)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (Char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length
                    || !Char.IsLowSurrogate(value[index + 1]))
                {
                    throw new InvalidDataException(
                        $"{label} contains an unpaired Unicode surrogate.");
                }

                index++;
            }
            else if (Char.IsLowSurrogate(character))
            {
                throw new InvalidDataException(
                    $"{label} contains an unpaired Unicode surrogate.");
            }
        }
    }

    private sealed record CanonicalProperty(
        string Name,
        JsonElement Value);
}
