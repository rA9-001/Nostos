using Nostos.Core;
using Nostos.Core.Abstractions;
using Nostos.Core.Journal;
using Nostos.Core.Safety;
using Nostos.Win32.Services;

namespace Nostos.Cli.Commands;

/// <summary>Read-only commands: nothing here changes the machine.</summary>
public static class CatalogCommands
{
    public static Task<int> ListAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var category = commandLine.Get("category");
        var maxRisk = commandLine.GetEnum<Risk>("risk") ?? Risk.Experimental;

        if (category is not null && TweakCategories.Find(category) is null)
        {
            Console.Error.WriteLine(
                $"Unknown category '{category}'. Run `nos categories` to see them.");
            return Task.FromResult(2);
        }

        // Everything is listed. Nothing is filtered out by how well evidenced it is -- that was
        // what the Folklore tier did, and a hidden entry helps nobody: the people who wanted it
        // went and found a .reg file instead.
        var tweaks = host.Engine.Registry.Query(category, maxRisk).ToList();

        // Measured, not a constant. A hard-coded column width is right until somebody adds a
        // tweak whose id is one character longer, and then every row it appears on is silently
        // ragged -- which is exactly how this got noticed.
        var idWidth = tweaks.Count == 0 ? 0 : tweaks.Max(t => t.Metadata.Id.Length);

        // Two levels of heading. The group answers "is this even about gaming", which is the
        // question that was impossible to answer from the old flat list, and the category
        // carries the promise so the grouping explains itself instead of being a bare word.
        foreach (var group in tweaks.GroupBy(t => TweakCategories.GroupOf(t.Metadata.Category)))
        {
            Console.WriteLine();
            Console.WriteLine(
                $"{Ansi.Bold}── {TweakCategories.NameOfGroup(group.Key).ToUpperInvariant()} " +
                $"{new string('─', Math.Max(0, 60 - TweakCategories.NameOfGroup(group.Key).Length))}{Ansi.Reset}");
            Console.WriteLine($"{Ansi.Dim}   {TweakCategories.DescriptionOfGroup(group.Key)}{Ansi.Reset}");

            foreach (var byCategory in group.GroupBy(t => t.Metadata.Category))
            {
                var info = TweakCategories.Get(byCategory.Key);

                Console.WriteLine();
                Console.WriteLine($"{Ansi.Bold}{info.Name.ToUpperInvariant()}{Ansi.Reset}  {Ansi.Dim}({info.Id}){Ansi.Reset}");
                Console.WriteLine($"{Ansi.Dim}{Wrap(info.Promise, 74, "  ")}{Ansi.Reset}");
                Console.WriteLine();

                foreach (var tweak in byCategory)
                {
                    var m = tweak.Metadata;
                    Console.WriteLine(
                        $"  {m.Id.PadRight(idWidth)} {Cell(RiskColour(m.Risk), Lower(m.Risk), 12)} " +
                        $"{Cell(EvidenceColour(m.Evidence), Lower(m.Evidence), 10)} " +
                        $"{m.Scope,-8} {m.Title}{(m.RequiresReboot ? $" {Ansi.Dim}(reboot){Ansi.Reset}" : "")}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tweaks.Count} tweak(s) shown.");

        return Task.FromResult(0);
    }

    /// <summary>
    /// Prints the categories and what each one claims, with a count, under their group.
    ///
    /// Exists so <c>--category</c> is discoverable without reading the source, and so the
    /// promises are somewhere a user can read them before deciding what to apply.
    /// </summary>
    public static Task<int> CategoriesAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var counts = host.Engine.Registry.All
            .GroupBy(t => t.Metadata.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Console.WriteLine();
        foreach (var group in Enum.GetValues<TweakGroup>())
        {
            Console.WriteLine($"{Ansi.Bold}── {TweakCategories.NameOfGroup(group).ToUpperInvariant()}{Ansi.Reset}");
            Console.WriteLine($"{Ansi.Dim}   {TweakCategories.DescriptionOfGroup(group)}{Ansi.Reset}");
            Console.WriteLine();

            foreach (var category in TweakCategories.InGroup(group))
            {
                counts.TryGetValue(category.Id, out var count);

                Console.WriteLine(
                    $"{Ansi.Bold}{category.Name}{Ansi.Reset}  {Ansi.Dim}{category.Id} - {count} tweak(s){Ansi.Reset}");
                Console.WriteLine(Wrap(category.Promise, 74, "  "));
                Console.WriteLine();
            }
        }

        Console.WriteLine($"{Ansi.Dim}Filter with `nos list --category <id>`.{Ansi.Reset}");
        Console.WriteLine();

        return Task.FromResult(0);
    }

    public static async Task<int> StatusAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var context = host.BuildContext(commandLine);
        var ids = commandLine.Positional.Skip(1).ToList();

        var subset = ids.Count == 0
            ? null
            : ids.Select(host.Engine.Registry.Get).ToList();

        var statuses = await host.Engine.GetStatusAsync(subset, context, ct).ConfigureAwait(false);

        // Unavailable rows sink to the bottom, under their own heading. They are not choices,
        // and interleaved they make every scan of the list step over things that cannot be
        // acted on.
        var ordered = statuses
            .OrderBy(s => s.Applicability.IsApplicable ? 0 : 1)
            .ThenBy(s => TweakCategories.GroupOf(s.Metadata.Category))
            .ThenBy(s => TweakCategories.OrderOf(s.Metadata.Category))
            .ThenBy(s => s.Metadata.Id, StringComparer.Ordinal)
            .ToList();

        var idWidth = ordered.Count == 0 ? 0 : ordered.Max(s => s.Metadata.Id.Length);

        Console.WriteLine();
        var printedUnavailableHeading = false;

        foreach (var status in ordered)
        {
            if (!status.Applicability.IsApplicable && !printedUnavailableHeading)
            {
                printedUnavailableHeading = true;
                Console.WriteLine();
                Console.WriteLine($"{Ansi.Bold}── NOT APPLICABLE ON THIS PC{Ansi.Reset}");
                Console.WriteLine(
                    $"{Ansi.Dim}   These need something this PC does not have. They all read off, "
                    + $"because none of them can be switched on.{Ansi.Reset}");
                Console.WriteLine();
            }

            // A tweak that cannot run here always prints off, whatever its own Read said: the
            // two are answered by independent methods and can disagree, and "on" beside
            // "[n/a]" is a contradiction the reader resolves by distrusting the marker.
            var shown = status.Applicability.IsApplicable && status.State.IsApplied;
            var marker = shown ? $"{Ansi.Green}on {Ansi.Reset}" : $"{Ansi.Dim}off{Ansi.Reset}";
            var managed = status.IsManagedByUs ? $" {Ansi.Cyan}[managed]{Ansi.Reset}" : "";
            var blocked = status.Applicability.IsApplicable
                ? ""
                : $" {Ansi.Yellow}[{status.Applicability.Reason}]{Ansi.Reset}";

            Console.WriteLine($"{marker} {status.Metadata.Id.PadRight(idWidth)}{managed}{blocked}");
            Console.WriteLine($"    {Ansi.Dim}{status.State.Description}{Ansi.Reset}");
        }

        return 0;
    }

    public static async Task<int> JournalAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var entries = await host.Journal.ReadAllAsync(ct).ConfigureAwait(false);
        var tail = commandLine.GetInt("tail") ?? 30;

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Dim}{host.Journal.GetType().Name} at {AppPaths.JournalPath}{Ansi.Reset}");
        Console.WriteLine();

        foreach (var entry in entries.TakeLast(tail))
        {
            var colour = entry.Action switch
            {
                JournalAction.ApplyCommitted => Ansi.Green,
                JournalAction.ApplyFailed or JournalAction.RevertFailed => Ansi.Red,
                JournalAction.RevertCommitted => Ansi.Cyan,
                _ => Ansi.Dim,
            };

            Console.WriteLine(
                $"{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss} {colour}{entry.Action,-16}{Ansi.Reset} " +
                $"{entry.TweakId,-34} {Ansi.Dim}{entry.Origin}{Ansi.Reset}");

            if (entry.Detail is not null)
                Console.WriteLine($"    {Ansi.Dim}{entry.Detail}{Ansi.Reset}");
            if (entry.Error is not null)
                Console.WriteLine($"    {Ansi.Red}{entry.Error}{Ansi.Reset}");
        }

        var outstanding = await host.Journal.GetOutstandingAsync(ct).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"{entries.Count} entries, {outstanding.Count} change(s) currently outstanding.");
        return 0;
    }

    /// <summary>Environment report. The first thing to ask a user for when they file an issue.</summary>
    public static async Task<int> DoctorAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}Environment{Ansi.Reset}");
        Line("Windows", $"{SystemInfo.OsVersion} (build {SystemInfo.Build}.{SystemInfo.UpdateBuildRevision}, {SystemInfo.DisplayVersion})");
        Line("Elevated", WindowsPrivilegeCheck.Instance.IsElevated ? "yes" : $"{Ansi.Yellow}no - machine-scope tweaks will be skipped{Ansi.Reset}");
        Line("Battery present", SystemInfo.HasBattery ? "yes (power tweaks are restricted)" : "no");
        Line("Timer resolution", $"{SystemInfo.CurrentTimerResolutionMs:0.000} ms");

        var sac = SystemInfo.SmartAppControl;
        var sacNote = sac switch
        {
            SmartAppControlState.Enforced =>
                $"{Ansi.Yellow}enforced - unsigned builds are blocked from running on this machine{Ansi.Reset}",
            SmartAppControlState.Evaluation => "evaluation",
            SmartAppControlState.Off => "off",
            _ => "unknown",
        };
        Line("Smart App Control", sacNote);

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}State{Ansi.Reset}");
        Line("Journal", AppPaths.JournalPath);

        var outstanding = await host.Journal.GetOutstandingAsync(ct).ConfigureAwait(false);
        Line("Outstanding changes", outstanding.Count.ToString());

        Line("Catalog size", $"{host.Engine.Registry.All.Count} tweaks");
        Console.WriteLine();
        return 0;
    }

    private static void Line(string label, string value)
        => Console.WriteLine($"  {label,-20} {value}");

    private static string Lower<T>(T value) where T : Enum
        => value.ToString().ToLowerInvariant();

    /// <summary>
    /// Pads to a column width using the <em>visible</em> length. Composite format alignment
    /// counts the ANSI escape bytes, which silently ragged the whole table.
    /// </summary>
    private static string Cell(string colour, string text, int width)
        => colour + text + Ansi.Reset + new string(' ', Math.Max(0, width - text.Length));

    private static string RiskColour(Risk risk) => risk switch
    {
        Risk.Safe => Ansi.Green,
        Risk.Moderate => Ansi.Yellow,
        _ => Ansi.Red,
    };

    private static string EvidenceColour(Evidence evidence) => evidence switch
    {
        Evidence.Measured => Ansi.Green,
        Evidence.Plausible => Ansi.Reset,
        _ => Ansi.Dim,
    };

    /// <summary>
    /// `nos show &lt;id&gt;` - everything about one tweak, including the options it offers.
    ///
    /// Exists because `--set level=aggressive` is unusable without a way to find out that
    /// "level" is a thing and what "aggressive" costs. The GUI shows this in its detail pane;
    /// this is the same information for people who never open it.
    /// </summary>
    public static async Task<int> ShowAsync(CliHost host, CommandLine commandLine, CancellationToken ct)
    {
        var id = commandLine.Positional.ElementAtOrDefault(1);
        if (id is null)
        {
            Console.Error.WriteLine("usage: nos show <tweak-id>");
            return 2;
        }

        var tweak = host.Engine.Registry.Find(id);
        if (tweak is null)
        {
            Console.Error.WriteLine($"No tweak with id '{id}'. Run `nos list --all` to see them.");
            return 1;
        }

        var m = tweak.Metadata;
        var context = host.BuildContext(commandLine);

        Console.WriteLine();
        Console.WriteLine($"{Ansi.Bold}{m.Title}{Ansi.Reset}");
        Console.WriteLine($"{Ansi.Dim}{m.Id}{Ansi.Reset}");
        Console.WriteLine();
        Console.WriteLine(Wrap(m.Summary, 76, "  "));
        Console.WriteLine();
        Console.WriteLine($"  Improves    {m.CategoryInfo.Name}  {Ansi.Dim}({m.Category}){Ansi.Reset}");
        Console.WriteLine($"{Ansi.Dim}{Wrap(m.CategoryInfo.Promise, 62, "              ")}{Ansi.Reset}");
        Console.WriteLine($"  Risk        {m.Risk.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Evidence    {m.Evidence.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Scope       {m.Scope.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  Reboot      {(m.RequiresReboot ? "required" : "no")}");
        Console.WriteLine($"  Docs        {m.DocsPath}");

        var applicability = await tweak.CheckApplicabilityAsync(context, ct).ConfigureAwait(false);
        if (!applicability.IsApplicable)
            Console.WriteLine($"  {Ansi.Yellow}Not here{Ansi.Reset}    {applicability.Reason}");

        foreach (var choice in m.Choices)
        {
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Bold}{choice.Title}{Ansi.Reset}  {Ansi.Dim}--set {choice.Id}=...{Ansi.Reset}");
            Console.WriteLine(Wrap(choice.Description, 76, "  "));
            Console.WriteLine();

            foreach (var option in choice.Options)
            {
                var marks = new List<string>();
                if (string.Equals(option.Id, choice.DefaultOption, StringComparison.OrdinalIgnoreCase))
                    marks.Add("default");
                if (option.Recommended)
                    marks.Add("recommended");

                var suffix = marks.Count > 0 ? $"  {Ansi.Green}({string.Join(", ", marks)}){Ansi.Reset}" : "";

                Console.WriteLine($"  {Ansi.Bold}{option.Id}{Ansi.Reset}  -  {option.Title}{suffix}");
                Console.WriteLine(Wrap(option.Description, 72, "      "));
                Console.WriteLine();
            }
        }

        if (m.Choices.Count == 0)
            Console.WriteLine();

        return 0;
    }

    /// <summary>Wraps text to a column, indenting every line. Console width is not assumed.</summary>
    private static string Wrap(string text, int width, string indent)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            lines.Add(line.ToString());

        return string.Join(Environment.NewLine, lines.Select(l => indent + l));
    }
}
