using Nostos.Core.Localization;

namespace Nostos.App.Localization;

/// <summary>
/// The one place that says "this needs administrator rights", in the reader's language.
///
/// Four tabs can hit the same wall -- a machine-wide cache, a machine-scope tweak, an HKLM
/// startup entry, a registration under HKLM -- and each of them used to handle it differently:
/// a button that disappeared, a control that did nothing, an error sentence written in a layer
/// that has no user and therefore no language. All three are the same bug wearing different
/// clothes, which is that the program knew the answer before the click and did not say so.
///
/// <para>The rule this class exists to enforce: <b>when the answer is knowable in advance, the
/// control stays and says what it needs.</b> A control that vanishes teaches somebody the
/// feature does not exist; one that fails afterwards teaches them the program is broken. Both
/// are worse than a button that says, before it is pressed, what would make it work.</para>
///
/// <para>What it never does is offer to elevate. Nostos runs as the user who started it and
/// asks for nothing more; the way to give it more is to start it with more, which is why every
/// sentence here ends by saying exactly that.</para>
/// </summary>
public static class AdminNeeded
{
    /// <summary>The badge beside whatever it annotates. Three words, so it fits on a row.</summary>
    public static string Badge => Strings.Get("admin.badge");

    /// <summary>
    /// What is wrong and what to do about it, for anything this process does itself:
    /// emptying a machine-wide cache, removing an HKLM key, deleting under Program Files.
    /// </summary>
    public static string Explain => Strings.Get("admin.explain");

    /// <summary>
    /// The same for a machine-wide change -- a tweak, or a startup entry every account sees --
    /// where the better answer is the background service.
    ///
    /// Different advice because the situation is different: these have a standing way to be
    /// made without an elevated window, which is the service: one approval, once. Sending
    /// somebody to restart the program as administrator instead would be telling them to do the
    /// awkward thing forever.
    /// </summary>
    public static string ExplainMachineWide => $"{Strings.Get("admin.explain.machine")} {RemedyMachineWide}";

    /// <summary>
    /// The answer on its own, for somewhere that has already stated the problem.
    ///
    /// A profile card says how many of its tweaks would be skipped, which is the same fact in
    /// better words; following it with "this change is machine-wide, and this copy cannot make
    /// it" made the card say it twice in a row. The remedy is one string either way, so the two
    /// places cannot drift into giving different advice.
    /// </summary>
    public static string RemedyMachineWide => Strings.Get("admin.remedy.machine");

    /// <summary>The one-line version for the status bar, where the whole sentence will not fit.</summary>
    public static string Status => Strings.Get("admin.status");

    /// <summary>What an elevated launch would additionally reach, once it has been measured.</summary>
    public static string More(string size) => Strings.Format("admin.more", size);
}
