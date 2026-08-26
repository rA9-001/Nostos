namespace Nostos.Core.Abstractions;

/// <summary>Seam over "am I running elevated", so the engine stays testable off-Windows.</summary>
public interface IPrivilegeCheck
{
    bool IsElevated { get; }
}

public sealed class AlwaysElevated : IPrivilegeCheck
{
    public static readonly AlwaysElevated Instance = new();
    public bool IsElevated => true;
}
