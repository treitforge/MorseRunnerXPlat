using System.Collections.ObjectModel;

namespace MorseRunner.Infrastructure;

[Flags]
public enum LegacySettingOperation
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Delete = 1 << 2,
    Exists = 1 << 3,
}

public sealed record LegacySettingDescriptor(
    string Section,
    string Key,
    LegacySettingOperation Operations);

public static class LegacySettingSchema
{
    private const LegacySettingOperation ReadWrite =
        LegacySettingOperation.Read | LegacySettingOperation.Write;

    private static readonly ReadOnlyCollection<LegacySettingDescriptor> Descriptors =
        Array.AsReadOnly<LegacySettingDescriptor>(
        [
            D("Band", "Activity", ReadWrite),
            D("Band", "Flutter", ReadWrite),
            D("Band", "Lids", ReadWrite),
            D("Band", "Qrm", ReadWrite),
            D("Band", "Qrn", ReadWrite),
            D("Band", "Qsb", ReadWrite),
            D("Contest", "CompetitionDuration", ReadWrite),
            D("Contest", "DefaultRunMode", ReadWrite),
            D("Contest", "Duration", ReadWrite),
            D("Contest", "HiScore", ReadWrite),
            D("Contest", "SimContest", ReadWrite),
            D("Debug", "AllStationsWpmS", LegacySettingOperation.Read),
            D("Debug", "DebugCwDecoder", LegacySettingOperation.Read),
            D("Debug", "DebugExchSettings", LegacySettingOperation.Read),
            D("Debug", "DebugGhosting", LegacySettingOperation.Read),
            D("Debug", "F8", LegacySettingOperation.Read),
            D("Settings", "FarnsworthCharacterRate", ReadWrite),
            D("Settings", "RitStepIncr", ReadWrite),
            D("Settings", "ShowCheckSection", ReadWrite),
            D("Settings", "ShowExchangeSummary", ReadWrite),
            D("Settings", "SingleCallStartDelay", ReadWrite),
            D("Settings", "StationIdRate", ReadWrite),
            D("Settings", "WpmStepRate", ReadWrite),
            D("Station", "AcagExchange", ReadWrite),
            D("Station", "AllJaExchange", ReadWrite),
            D("Station", "ArrlClass", ReadWrite),
            D("Station", "ArrlDxExchange", ReadWrite),
            D("Station", "ArrlFdExchange", ReadWrite),
            D("Station", "ArrlSection", ReadWrite),
            D("Station", "BandWidth", ReadWrite),
            D("Station", "Call", ReadWrite),
            D("Station", "CallsFromKeyer", LegacySettingOperation.Read),
            D("Station", "CqWpxExchange", ReadWrite),
            D("Station", "CQWWExchange", ReadWrite),
            D("Station", "CWMaxRxSpeed", ReadWrite),
            D("Station", "CWMinRxSpeed", ReadWrite),
            D("Station", "cwopsnum", LegacySettingOperation.Delete),
            D("Station", "CwtExchange", ReadWrite),
            D("Station", "GetWpmUsesGaussian", LegacySettingOperation.Read),
            D("Station", "HSTExchange", ReadWrite),
            D("Station", "IaruHfExchange", ReadWrite),
            D("Station", "Name", LegacySettingOperation.Read),
            D("Station", "NAQPExchange", ReadWrite),
            D(
                "Station",
                "NRDigits",
                LegacySettingOperation.Read
                    | LegacySettingOperation.Delete
                    | LegacySettingOperation.Exists),
            D("Station", "Pitch", ReadWrite),
            D("Station", "Qsk", ReadWrite),
            D("Station", "SaveWav", ReadWrite),
            D("Station", "SelfMonVolume", ReadWrite),
            D("Station", "SerialNR", ReadWrite),
            D("Station", "SerialNrCustomRange", ReadWrite),
            D("Station", "SerialNrEndContest", LegacySettingOperation.Read),
            D("Station", "SerialNrMidContest", LegacySettingOperation.Read),
            D("Station", "SSCWExchange", ReadWrite),
            D("Station", "SstExchange", ReadWrite),
            D("Station", "Wpm", ReadWrite),
            D("System", "BufSize", ReadWrite),
            D("System", "PostMethod", LegacySettingOperation.Read),
            D("System", "ShowCallsignInfo", ReadWrite),
            D("System", "SubmitHiScoreURL", LegacySettingOperation.Read),
            D("System", "WebServer", LegacySettingOperation.Read),
        ]);

    public static IReadOnlyList<LegacySettingDescriptor> All => Descriptors;

    public static bool TryGet(
        string section,
        string key,
        out LegacySettingDescriptor? descriptor)
    {
        descriptor = Descriptors.SingleOrDefault(
            item => string.Equals(
                    item.Section,
                    section,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Key,
                    key,
                    StringComparison.OrdinalIgnoreCase));
        return descriptor is not null;
    }

    private static LegacySettingDescriptor D(
        string section,
        string key,
        LegacySettingOperation operations) =>
        new(section, key, operations);
}
