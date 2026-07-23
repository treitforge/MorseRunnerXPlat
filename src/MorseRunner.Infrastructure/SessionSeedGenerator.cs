using System.Security.Cryptography;

namespace MorseRunner.Infrastructure;

public static class SessionSeedGenerator
{
    public static int Create() => RandomNumberGenerator.GetInt32(1, Int32.MaxValue);
}
