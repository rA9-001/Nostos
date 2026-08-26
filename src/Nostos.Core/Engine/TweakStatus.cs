using Nostos.Core.Abstractions;

namespace Nostos.Core.Engine;

/// <param name="Metadata">Static description.</param>
/// <param name="State">Live value read from the machine.</param>
/// <param name="IsManagedByUs">True when the journal holds an un-reverted snapshot for this tweak.</param>
/// <param name="Applicability">Whether the tweak can work on this machine at all.</param>
public sealed record TweakStatus(
    TweakMetadata Metadata,
    TweakState State,
    bool IsManagedByUs,
    Applicability Applicability);
