using Microsoft.Win32;

namespace Nostos.Win32.Apps;

/// <summary>
/// The icon Windows Installer holds for a product it installed.
///
/// This is the source that was missing, and it is the one Programs and Features itself uses. An
/// MSI carries its artwork as a property inside the package rather than as a file on disk, so an
/// MSI-installed program very often has no <c>DisplayIcon</c> in its uninstall key, no
/// <c>InstallLocation</c>, and nothing else to point an icon search at -- Epic Games Launcher on
/// this machine has all three absent. What it does have is an entry in the installer's own
/// product database, where Windows cached the icon at install time under
/// <c>%WINDIR%\Installer\{ProductCode}\</c>.
///
/// <para>The awkward part is the key name. Products are filed under a <em>packed</em> product
/// code -- the GUID with its first three fields reversed and the rest byte-swapped -- so
/// <c>{D5B1B157-0F47-4E06-9C9A-97BD8A3E6039}</c> is stored as
/// <c>751B1B5D74F060E4C9A979DBA8E30693</c>. It is a fixed transform rather than a hash, so it can
/// be computed rather than searched for, which matters: the alternative is enumerating several
/// hundred product keys for every row in the list.</para>
/// </summary>
public static class MsiProducts
{
    /// <summary>Machine-wide installs. Readable without elevation.</summary>
    private const string MachineProducts = @"SOFTWARE\Classes\Installer\Products";

    /// <summary>Per-user installs, which are filed somewhere else entirely.</summary>
    private const string UserProducts = @"SOFTWARE\Microsoft\Installer\Products";

    /// <summary>
    /// The cached icon for an MSI product code, or null when it has none or the file has gone.
    /// </summary>
    /// <param name="productCode">The braced GUID out of an <c>MsiExec.exe /X{...}</c> command.</param>
    public static string? FindIcon(string? productCode)
    {
        if (Pack(productCode) is not { } packed)
            return null;

        return IconIn(Registry.LocalMachine, $@"{MachineProducts}\{packed}")
               ?? IconIn(Registry.CurrentUser, $@"{UserProducts}\{packed}");
    }

    /// <summary>
    /// A product's ProductIcon value, checked against the disk.
    ///
    /// The existence check is the point of doing this here rather than at the caller. A stale
    /// path -- the product repaired, the cache cleaned out -- has to read as "no icon" so the
    /// search moves on to the next source. Returning it anyway would end the search on a file
    /// that is not there, and the row would fall back to a monogram it did not need.
    /// </summary>
    private static string? IconIn(RegistryKey hive, string path)
    {
        try
        {
            using var key = hive.OpenSubKey(path);

            if (key?.GetValue("ProductIcon") as string is not { Length: > 0 } value)
                return null;

            // Shares the ,index handling with DisplayIcon, because it is the same convention:
            // several products point at an executable and name which of its icons they mean.
            var icon = InstalledApps.IconFrom(Environment.ExpandEnvironmentVariables(value));

            return icon is not null && File.Exists(icon) ? icon : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// A product code in the form the installer's registry files it under, or null if it is not
    /// a GUID.
    ///
    /// The first three fields of a GUID are stored little-endian, so packing reverses each of
    /// them whole; the last two are stored as they are, so those are swapped a byte at a time.
    /// That difference is the whole transform, and getting it wrong produces a key that simply
    /// does not exist -- which looks exactly like a product with no icon, so it is worth a test
    /// with a known pair rather than trusting a reading of the layout.
    /// </summary>
    public static string? Pack(string? productCode)
    {
        if (productCode is not { Length: > 0 } || !Guid.TryParse(productCode, out var guid))
            return null;

        var digits = guid.ToString("N").ToUpperInvariant();
        var packed = new System.Text.StringBuilder(32);

        foreach (var (start, length) in new[] { (0, 8), (8, 4), (12, 4) })
        {
            for (var i = start + length - 1; i >= start; i--)
                packed.Append(digits[i]);
        }

        for (var i = 16; i + 1 < digits.Length; i += 2)
        {
            packed.Append(digits[i + 1]);
            packed.Append(digits[i]);
        }

        return packed.ToString();
    }
}
