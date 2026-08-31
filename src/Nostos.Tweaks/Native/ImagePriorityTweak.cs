using System.Diagnostics;
using System.Text.Json.Nodes;
using Nostos.Core.Abstractions;
using Nostos.Win32.Services;

namespace Nostos.Tweaks.Native;

/// <summary>
/// Gives an executable a CPU priority that Windows applies every time it starts.
///
/// The companion to process.game-tuning, and the answer to its one real complaint: that tweak
/// changes a process that is already running, so it has to be re-applied after every launch,
/// and nobody is going to open a window before each match to do that. This writes a value the
/// loader reads at process creation instead, so the game simply comes up at the right priority
/// with nothing running to arrange it.
///
/// The cost of that permanence is where the two differ in risk. game-tuning is Safe because the
/// worst case is a process exiting; this one survives reboots, applies to every copy of an image
/// name anywhere on the machine, and takes effect before anything -- including this program --
/// is in a position to intervene. Hence Moderate, and hence no Realtime and no Idle: the option
/// list is the same two useful classes game-tuning offers.
///
/// It deliberately does not touch EcoQoS. There is no loader-level equivalent, so the honest
/// division is that this handles the half that can be made permanent and game-tuning handles the
/// half that cannot.
/// </summary>
public sealed class ImagePriorityTweak : ITweak
{
    public TweakMetadata Metadata { get; } = new()
    {
        Id = "process.persistent-priority",
        Title = "Always start a game at a higher priority",
        Summary = "Records a CPU priority for the game's executable that Windows applies every " +
                  "time it launches, so it does not have to be set again after each restart.",
        Category = TweakCategories.Performance,
        // Machine, not Process: the value lives in HKLM and outlives every process it affects.
        // It is pointed at a process only because that is the convenient way to name an image.
        Scope = TweakScope.Machine,
        Lifetime = TweakLifetime.Persistent,
        Risk = Risk.Moderate,
        Evidence = Evidence.Plausible,
        // Not a reboot: it takes effect the next time the game starts, which is sooner and is
        // said in the state description rather than claimed here.
        RequiresReboot = false,
        RequiresElevation = true,
        TakesTargetProcess = true,
        Tags = ["priority", "ifeo", "persistent", "startup"],
        Choices =
        [
            new TweakChoice
            {
                Id = "priority",
                Title = "Scheduling priority",
                Description =
                    "Where the game sits in the scheduler's queue relative to everything else " +
                    "running, from the moment it starts.",
                DefaultOption = "abovenormal",
                Options =
                [
                    new TweakChoiceOption
                    {
                        Id = "abovenormal",
                        Title = "Above normal",
                        Description =
                            "A gentle nudge ahead of ordinary background work. The safer pick " +
                            "for a permanent setting, and for a machine you also work on.",
                        Recommended = true,
                    },
                    new TweakChoiceOption
                    {
                        Id = "high",
                        Title = "High",
                        Description =
                            "Ahead of essentially everything except the system itself. Worth " +
                            "knowing that a game which hangs at High is harder to click away " +
                            "from than one at Above normal, and this setting applies every launch.",
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// Which executable this operation is about.
    ///
    /// Two ways in, because the two callers know different things. The window has a process
    /// picker and hands over a running process's name; the command line can name an image that
    /// is not running at all, which is the case the picker cannot cover and the more useful one
    /// for setting a game up before playing it.
    /// </summary>
    private static string? ImageName(TweakContext context)
    {
        var explicitly = context.GetString("exe");
        var name = string.IsNullOrWhiteSpace(explicitly) ? context.TargetProcessName : explicitly;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            return ImagePriority.NormaliseImageName(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The image, or a refusal. Only Apply uses this: Read answers about the machine as a whole
    /// when it is not asked about a particular game, and Capture records the whole machine
    /// regardless.
    /// </summary>
    private static string RequireImage(TweakContext context)
        => ImageName(context)
           ?? throw new InvalidOperationException(
               "No target executable; pick a running program, or pass --set exe=game.exe.");

    private static ProcessPriorityClass DesiredPriority(TweakContext context)
    {
        var requested = context.GetString("priority") ?? "abovenormal";

        if (!Enum.TryParse<ProcessPriorityClass>(requested, ignoreCase: true, out var parsed))
            throw new ArgumentException($"Unknown priority class '{requested}'.");

        // Idle and BelowNormal are reachable in the registry and are not offered here. A
        // permanent setting that makes a game slower every launch is not a thing the catalog
        // should be able to do by accident, and Realtime is refused for the reasons
        // process.game-tuning gives -- more so, since this one applies before anything can help.
        if (parsed is not (ProcessPriorityClass.AboveNormal or ProcessPriorityClass.High))
        {
            throw new ArgumentException(
                $"'{requested}' is not offered as a permanent priority. This tweak sets Above "
                + "normal or High; anything lower makes the game slower every time it starts, "
                + "and Realtime can hang the desktop before anything is running to stop it.");
        }

        return parsed;
    }

    public Task<Applicability> CheckApplicabilityAsync(TweakContext context, CancellationToken ct = default)
        => Task.FromResult(ImageName(context) is null
            ? Applicability.No(
                "notapplicable.noexe",
                "no target executable specified: pick a running program, or pass exe=game.exe")
            : Applicability.Applicable);

    public Task<TweakState> ReadAsync(TweakContext context, CancellationToken ct = default)
    {
        // Asked about no game in particular, which is what a revert does when it reads the
        // machine back afterwards. Answering with the whole picture is both the useful answer
        // and the only one that does not turn a successful revert into a line reading like a
        // failure -- which is exactly what throwing here used to do.
        if (ImageName(context) is not { } image)
        {
            var configured = ImagePriority.All();
            return Task.FromResult(new TweakState(
                IsApplied: false,
                configured.Count == 0
                    ? "no executable has a permanent priority on this machine"
                    : $"{configured.Count} executable(s) have a permanent priority: "
                      + string.Join(", ", configured.Keys.Order(StringComparer.OrdinalIgnoreCase)),
                new JsonObject { ["images"] = configured.Count }));
        }

        var current = ImagePriority.Read(image);
        var desired = ImagePriority.ToIfeo(DesiredPriority(context));

        // Counts as applied only for the image being asked about. The same tweak can be set for
        // several games at once, and each one is its own question.
        var isApplied = current == desired;

        var description = current is { } value
            ? $"{image}: always starts at {ImagePriority.Describe(value)}"
            : $"{image}: no permanent priority set (starts at whatever launched it asks for)";

        return Task.FromResult(new TweakState(isApplied, description, new JsonObject
        {
            ["image"] = image,
            ["current"] = current,
            ["desired"] = desired,
        }));
    }

    /// <summary>
    /// Captures every permanent priority on the machine, not just the one being set.
    ///
    /// This is the one place this tweak departs from the usual shape, and it is forced by the
    /// tweak being set once per game while the journal keeps one outstanding snapshot per tweak
    /// id -- the oldest, so that applying twice still reverts to the original machine. If the
    /// snapshot held only the image being set, then setting cs2 and then valorant would leave
    /// the second one with no record and no way back: permanent, machine-wide, and invisible.
    ///
    /// Capturing the whole map makes the oldest-snapshot rule do exactly the right thing. The
    /// first apply records "these images had permanent priorities, and these are their values",
    /// which for most machines is the empty set, and reverting restores precisely that however
    /// many games were set in between.
    /// </summary>
    public Task<TweakSnapshot> CaptureAsync(TweakContext context, CancellationToken ct = default)
    {
        var images = new JsonObject();
        foreach (var (image, value) in ImagePriority.All())
            images[image] = value;

        return Task.FromResult(TweakSnapshot.Create(Metadata.Id, new JsonObject
        {
            // The image this particular apply is about, for the journal to read back. Not what
            // revert uses -- that is "images" -- but what makes the entry legible.
            ["image"] = ImageName(context),
            ["images"] = images,
        }));
    }

    public Task ApplyAsync(TweakContext context, CancellationToken ct = default)
    {
        var image = RequireImage(context);
        var priority = DesiredPriority(context);

        ImagePriority.Set(image, ImagePriority.ToIfeo(priority));
        context.Log.Info(
            $"{Metadata.Id}: {image} -> {priority} on every launch (takes effect next start)");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Puts the machine's permanent priorities back exactly as they were captured.
    ///
    /// Note what this means for somebody who set three games: reverting undoes all three, not
    /// the one currently selected. That follows from the snapshot being the whole map, it is the
    /// only behaviour that leaves nothing stranded, and the docs page says so plainly. Removing
    /// a single game is `nos revert` for all of them and setting the others again, or deleting
    /// its key by hand.
    /// </summary>
    public Task RevertAsync(TweakSnapshot snapshot, TweakContext context, CancellationToken ct = default)
    {
        if (Captured(snapshot) is not { } captured)
        {
            // No map in the snapshot at all: truncated, hand-edited, or written by a build from
            // before this field existed. "We do not know what was there" is emphatically not the
            // same as "nothing was there", and an empty map here would clear every permanent
            // priority on the machine -- including ones nothing to do with this program.
            context.Log.Warn(
                $"{Metadata.Id}: the journal entry has no record of what was set, so there is "
                + "nothing safe to restore. Remove the entries under Image File Execution "
                + "Options by hand if they are unwanted.");

            return Task.CompletedTask;
        }

        foreach (var (image, value) in RevertPlan(captured, ImagePriority.All()))
        {
            if (value is { } restore)
                ImagePriority.Set(image, restore);
            else
                ImagePriority.Clear(image);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the captured map back out of a snapshot.
    ///
    /// Null when the snapshot has no map -- which is a different thing from an empty map, and
    /// the distinction matters: an empty map means the machine had no permanent priorities and
    /// revert should remove all of them, while no map means revert has no idea and must not.
    /// </summary>
    public static IReadOnlyDictionary<string, int>? Captured(TweakSnapshot snapshot)
    {
        if (snapshot.Data["images"] is not JsonObject stored)
            return null;

        var captured = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (image, value) in stored)
        {
            if (value?.GetValue<int>() is { } number)
                captured[image] = number;
        }

        return captured;
    }

    /// <summary>
    /// What revert has to do to turn <paramref name="current"/> back into <paramref name="captured"/>.
    ///
    /// A null value means "remove this one". Separated from the registry so the reconciliation
    /// can be tested without a machine to test it on -- and because it is the part worth getting
    /// right: an image set after the capture has to be removed, or it stays permanently.
    /// </summary>
    public static IReadOnlyList<(string Image, int? Value)> RevertPlan(
        IReadOnlyDictionary<string, int> captured,
        IReadOnlyDictionary<string, int> current)
    {
        var plan = new List<(string, int?)>();

        // Anything set now that was not set then goes, including every image this tweak was
        // pointed at after the capture. That is the case the whole-map snapshot exists for.
        foreach (var image in current.Keys)
        {
            if (!captured.ContainsKey(image))
                plan.Add((image, null));
        }

        // And anything that was set then is put back to the value it had, whether it has since
        // been changed or removed. Restores another tool's setting as faithfully as our own.
        foreach (var (image, value) in captured)
        {
            if (!current.TryGetValue(image, out var now) || now != value)
                plan.Add((image, value));
        }

        return plan;
    }

    public async Task<bool> VerifyAsync(TweakContext context, CancellationToken ct = default)
        => (await ReadAsync(context, ct).ConfigureAwait(false)).IsApplied;
}
