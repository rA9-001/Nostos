using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Nostos.Win32.Services;

/// <summary>Fully-qualified pointer to a single registry value.</summary>
/// <param name="Hive">"HKLM", "HKCU", "HKCR", "HKU".</param>
/// <param name="SubKey">Path below the hive, e.g. @"SOFTWARE\Microsoft\...".</param>
/// <param name="Name">Value name. Empty string means the key's default value.</param>
public sealed record RegistryValueRef(string Hive, string SubKey, string Name)
{
    public override string ToString() => $@"{Hive}\{SubKey}\{Name}";
}

/// <summary>
/// Registry read/write with snapshot support.
///
/// The snapshot distinguishes "value was 20" from "value did not exist", because those revert
/// differently: the first restores 20, the second deletes the value we created. Conflating them
/// is how tools leave junk behind after an uninstall.
/// </summary>
public static class RegistryAccess
{
    public static RegistryKey OpenBase(string hive)
    {
        var baseKey = hive.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            _ => throw new ArgumentException($"Unsupported hive '{hive}'.", nameof(hive)),
        };

        // Always the 64-bit view: several graphics and multimedia keys exist only there, and
        // silently landing in Wow6432Node produces a tweak that "applies" and does nothing.
        return RegistryKey.OpenBaseKey(baseKey, RegistryView.Registry64);
    }

    public static (object? Value, RegistryValueKind Kind) Read(RegistryValueRef reference)
    {
        using var baseKey = OpenBase(reference.Hive);
        using var key = baseKey.OpenSubKey(reference.SubKey, writable: false);
        if (key is null)
            return (null, RegistryValueKind.Unknown);

        var value = key.GetValue(reference.Name, defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
            return (null, RegistryValueKind.Unknown);

        return (value, key.GetValueKind(reference.Name));
    }

    public static void Write(RegistryValueRef reference, object value, RegistryValueKind kind)
    {
        using var baseKey = OpenBase(reference.Hive);
        using var key = baseKey.CreateSubKey(reference.SubKey, writable: true)
            ?? throw new InvalidOperationException($"Could not open or create {reference.Hive}\\{reference.SubKey}.");
        key.SetValue(reference.Name, value, kind);
    }

    public static void DeleteValue(RegistryValueRef reference)
    {
        using var baseKey = OpenBase(reference.Hive);
        using var key = baseKey.OpenSubKey(reference.SubKey, writable: true);
        key?.DeleteValue(reference.Name, throwOnMissingValue: false);
    }

    /// <summary>Captures the current value in a form the journal can round-trip through JSON.</summary>
    public static JsonObject Capture(RegistryValueRef reference)
    {
        var (value, kind) = Read(reference);
        return new JsonObject
        {
            ["hive"] = reference.Hive,
            ["subKey"] = reference.SubKey,
            ["name"] = reference.Name,
            ["existed"] = value is not null,
            ["kind"] = kind.ToString(),
            ["value"] = value is null ? null : Encode(value, kind),
        };
    }

    /// <summary>Restores a value captured by <see cref="Capture"/>, including deleting one we created.</summary>
    public static void Restore(JsonObject snapshot)
    {
        var reference = new RegistryValueRef(
            snapshot["hive"]?.GetValue<string>() ?? throw new InvalidDataException("snapshot missing 'hive'"),
            snapshot["subKey"]?.GetValue<string>() ?? throw new InvalidDataException("snapshot missing 'subKey'"),
            snapshot["name"]?.GetValue<string>() ?? "");

        if (snapshot["existed"]?.GetValue<bool>() != true)
        {
            DeleteValue(reference);
            return;
        }

        var kind = Enum.Parse<RegistryValueKind>(
            snapshot["kind"]?.GetValue<string>() ?? nameof(RegistryValueKind.String));
        var encoded = snapshot["value"]?.GetValue<string>() ?? "";
        Write(reference, Decode(encoded, kind), kind);
    }

    public static string Encode(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.ToHexString((byte[])value),
        RegistryValueKind.MultiString => string.Join('\n', (string[])value),
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// The same value as a person should read it, which is not always how it is stored.
    ///
    /// <see cref="Encode"/> is the snapshot and comparison format, so it has to stay stable and
    /// exactly round-trippable, and it renders every DWORD as a signed decimal. That is
    /// unreadable for the ones that are really unsigned bitmasks: <c>0x80000001</c> comes back
    /// as <c>-2147483647</c>, which a reader takes for a bug rather than a setting.
    ///
    /// A DWORD with the top bit set is a mask or a sentinel, never a count -- no real tweak
    /// asks for minus two billion of anything -- so those are shown as hex and everything else
    /// is left alone.
    /// </summary>
    public static string Describe(object value, RegistryValueKind kind)
    {
        if (kind != RegistryValueKind.DWord)
            return Encode(value, kind);

        var dword = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return dword < 0 ? $"0x{(uint)dword:X8}" : Encode(value, kind);
    }

    public static object Decode(string encoded, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => ParseDword(encoded),
        RegistryValueKind.QWord => long.Parse(encoded, CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromHexString(encoded),
        RegistryValueKind.MultiString => encoded.Split('\n'),
        _ => encoded,
    };

    /// <summary>
    /// Parses a DWORD literal from a catalog entry or a snapshot.
    ///
    /// Accepts "10", "0xFFFFFFFF" and "4294967295" alike: several real tweaks are documented as
    /// unsigned hex (NetworkThrottlingIndex is the usual example) while the registry API takes a
    /// signed int, and forcing contributors to write "-1" invites transcription mistakes.
    /// </summary>
    public static int ParseDword(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((int)uint.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed))
            return signed;

        return unchecked((int)uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    /// <summary>Reads a DWORD, returning null when the key or value is absent.</summary>
    public static int? ReadDword(RegistryValueRef reference)
    {
        var (value, kind) = Read(reference);
        return value is not null && kind == RegistryValueKind.DWord
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : null;
    }
}
