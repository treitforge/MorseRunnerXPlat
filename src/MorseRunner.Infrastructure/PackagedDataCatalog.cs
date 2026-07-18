using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;

namespace MorseRunner.Infrastructure;

public sealed record PackagedDataFile(string Name, long Length, string Sha256);

public sealed class PackagedDataCatalog
{
    private const string ResourcePrefix = "MorseRunner.Infrastructure.Data.";
    private readonly Assembly _assembly = typeof(PackagedDataCatalog).Assembly;
    private readonly ReadOnlyCollection<string> _fileNames;

    public PackagedDataCatalog()
    {
        _fileNames = Array.AsReadOnly(
            _assembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .Select(name => name[ResourcePrefix.Length..])
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public IReadOnlyList<string> FileNames => _fileNames;

    public Stream OpenRequired(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string canonicalName = _fileNames.SingleOrDefault(
            name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException(
                $"Packaged reference data '{fileName}' was not found.",
                fileName);
        return _assembly.GetManifestResourceStream(ResourcePrefix + canonicalName)
            ?? throw new InvalidOperationException(
                $"Packaged resource '{canonicalName}' could not be opened.");
    }

    public TextReader OpenTextRequired(string fileName)
    {
        return new StreamReader(OpenRequired(fileName));
    }

    public PackagedDataFile Describe(string fileName)
    {
        using Stream stream = OpenRequired(fileName);
        byte[] hash = SHA256.HashData(stream);
        return new PackagedDataFile(
            _fileNames.Single(
                name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)),
            stream.Length,
            Convert.ToHexString(hash));
    }
}
