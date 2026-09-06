using System.Runtime.InteropServices;
using System.Text;

namespace Nostos.Win32.Apps;

/// <summary>
/// Resolves an MSIX package's display name out of its compiled resource index.
///
/// A package stores its name as a reference, not as text -- literally "ms-resource:AppStoreName"
/// in the registry -- because the real string is language-dependent and lives in the package's
/// resources.pri. <c>SHLoadIndirectString</c> is the documented way to turn one into words.
///
/// The catch, and the reason this is its own file: the form the registry holds is not the form
/// the API accepts. `ms-resource:AppStoreName` on its own fails; it has to be rebuilt as
/// `@{&lt;PackageFullName&gt;?ms-resource://&lt;PackageName&gt;/resources/&lt;Key&gt;}` first.
/// Verified against Microsoft.AV1VideoExtension, which the shorter forms could not resolve.
/// </summary>
internal static class IndirectString
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string source, StringBuilder outBuffer, int outBufferSize, IntPtr reserved);

    /// <summary>The resolved name, or null when the package cannot supply one.</summary>
    /// <param name="packageFullName">e.g. Microsoft.AV1VideoExtension_2.0.7.0_x64__8wekyb3d8bbwe</param>
    /// <param name="identity">The part before the first underscore.</param>
    /// <param name="stored">Whatever the registry had, e.g. "ms-resource:AppStoreName".</param>
    internal static string? Resolve(string packageFullName, string identity, string stored)
    {
        foreach (var candidate in Candidates(packageFullName, identity, stored))
        {
            if (TryLoad(candidate) is { Length: > 0 } resolved
                && !resolved.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// The forms worth trying, in order.
    ///
    /// A package whose stored value is already a full ms-resource:// URI needs only wrapping; one
    /// holding a bare key needs the package name and the resources/ path put back around it.
    /// Both appear on an ordinary machine.
    /// </summary>
    internal static IEnumerable<string> Candidates(string packageFullName, string identity, string stored)
    {
        if (stored.StartsWith("@{", StringComparison.Ordinal))
        {
            yield return stored;
            yield break;
        }

        if (stored.StartsWith("ms-resource://", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"@{{{packageFullName}?{stored}}}";
            yield break;
        }

        var key = stored.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? stored["ms-resource:".Length..].TrimStart('/')
            : stored;

        yield return $"@{{{packageFullName}?ms-resource://{identity}/resources/{key}}}";
        yield return $"@{{{packageFullName}?ms-resource://{identity}/{key}}}";
    }

    private static string? TryLoad(string source)
    {
        var buffer = new StringBuilder(1024);

        try
        {
            return SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero) == 0
                ? buffer.ToString()
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
