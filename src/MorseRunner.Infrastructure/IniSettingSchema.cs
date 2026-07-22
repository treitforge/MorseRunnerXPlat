using System.Collections.ObjectModel;

namespace MorseRunner.Infrastructure;

[Flags]
public enum IniSettingOperation
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    Delete = 1 << 2,
    Exists = 1 << 3,
}

public sealed record IniSettingDescriptor(
    string Section,
    string Key,
    IniSettingOperation Operations);

public static class IniSettingSchema
{
    private const IniSettingOperation ReadWrite =
        IniSettingOperation.Read | IniSettingOperation.Write;

    private static readonly ReadOnlyCollection<IniSettingDescriptor> Descriptors =
        Array.AsReadOnly<IniSettingDescriptor>(
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
            D("Debug", "AllStationsWpmS", IniSettingOperation.Read),
            D("Debug", "DebugCwDecoder", IniSettingOperation.Read),
            D("Debug", "DebugExchSettings", IniSettingOperation.Read),
            D("Debug", "DebugGhosting", IniSettingOperation.Read),
            D("Debug", "F8", IniSettingOperation.Read),
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
            D("Station", "CallsFromKeyer", IniSettingOperation.Read),
            D("Station", "CqWpxExchange", ReadWrite),
            D("Station", "CQWWExchange", ReadWrite),
            D("Station", "CWMaxRxSpeed", ReadWrite),
            D("Station", "CWMinRxSpeed", ReadWrite),
            D("Station", "cwopsnum", IniSettingOperation.Delete),
            D("Station", "CwtExchange", ReadWrite),
            D("Station", "GetWpmUsesGaussian", IniSettingOperation.Read),
            D("Station", "HSTExchange", ReadWrite),
            D("Station", "IaruHfExchange", ReadWrite),
            D("Station", "Name", IniSettingOperation.Read),
            D("Station", "NAQPExchange", ReadWrite),
            D(
                "Station",
                "NRDigits",
                IniSettingOperation.Read
                    | IniSettingOperation.Delete
                    | IniSettingOperation.Exists),
            D("Station", "Pitch", ReadWrite),
            D("Station", "Qsk", ReadWrite),
            D("Station", "SaveWav", ReadWrite),
            D("Station", "SelfMonVolume", ReadWrite),
            D("Station", "SerialNR", ReadWrite),
            D("Station", "SerialNrCustomRange", ReadWrite),
            D("Station", "SerialNrEndContest", IniSettingOperation.Read),
            D("Station", "SerialNrMidContest", IniSettingOperation.Read),
            D("Station", "SSCWExchange", ReadWrite),
            D("Station", "SstExchange", ReadWrite),
            D("Station", "Wpm", ReadWrite),
            D("System", "BufSize", ReadWrite),
            D("System", "PostMethod", IniSettingOperation.Read),
            D("System", "ShowCallsignInfo", ReadWrite),
            D("System", "SubmitHiScoreURL", IniSettingOperation.Read),
            D("System", "WebServer", IniSettingOperation.Read),
        ]);

    public static IReadOnlyList<IniSettingDescriptor> All => Descriptors;

    public static bool TryGet(
        string section,
        string key,
        out IniSettingDescriptor? descriptor)
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

    private static IniSettingDescriptor D(
        string section,
        string key,
        IniSettingOperation operations) =>
        new(section, key, operations);
}
