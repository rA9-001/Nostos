using Nostos.Core.Abstractions;
using Nostos.Core.Profiles;
using Nostos.Tweaks;
using Nostos.Tweaks.Declarative;

namespace Nostos.Tweaks.Tests;

/// <summary>
/// Rules the catalog has to obey. These run in CI, so a pull request that adds a tweak without
/// a docs page or with a made-up evidence claim fails before a human has to notice.
/// </summary>
public sealed class CatalogIntegrityTests
{
    private static readonly IReadOnlyList<ITweak> All = CatalogFactory.CreateAll();

    /// <summary>Walks up from the test binary to the repo root so the docs check works from any CWD.</summary>
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nostos.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    public static TheoryData<string> TweakIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var tweak in All)
                data.Add(tweak.Metadata.Id);
            return data;
        }
    }

    [Fact]
    public void The_catalog_is_not_empty() => Assert.NotEmpty(All);

    [Fact]
    public void Tweak_ids_are_unique()
    {
        var duplicates = All
            .GroupBy(t => t.Metadata.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(TweakIds))]
    public void Every_tweak_has_a_docs_page(string id)
    {
        var path = Path.Combine(RepoRoot, "docs", "tweaks", $"{id}.md");

        Assert.True(File.Exists(path),
            $"Tweak '{id}' has no docs page. Create docs/tweaks/{id}.md explaining what it " +
            "changes, the mechanism, and why it carries the evidence rating it claims.");
    }

    [Theory]
    [MemberData(nameof(TweakIds))]
    public void Every_docs_page_states_the_evidence_rating(string id)
    {
        var tweak = All.Single(t => t.Metadata.Id == id);
        var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", "tweaks", $"{id}.md"));

        Assert.Contains(tweak.Metadata.Evidence.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ids_are_lowercase_dotted_slugs()
    {
        foreach (var tweak in All)
        {
            Assert.Matches("^[a-z0-9]+(\\.[a-z0-9-]+)+$", tweak.Metadata.Id);
        }
    }

    [Fact]
    public void Every_tweak_has_a_category_and_a_summary()
    {
        foreach (var tweak in All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.Metadata.Category), tweak.Metadata.Id);
            Assert.False(string.IsNullOrWhiteSpace(tweak.Metadata.Summary), tweak.Metadata.Id);
        }
    }

    /// <summary>
    /// A category with nothing in it is a promise the tool does not keep. Deleting the last
    /// tweak in one should either bring the bucket down with it or be a deliberate decision,
    /// not something discovered by a user clicking an empty filter.
    /// </summary>
    [Fact]
    public void Every_category_has_at_least_one_tweak()
    {
        var used = All.Select(t => t.Metadata.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var empty = TweakCategories.All.Where(c => !used.Contains(c.Id)).Select(c => c.Id).ToList();

        Assert.True(empty.Count == 0,
            $"Categories with no tweaks in them: {string.Join(", ", empty)}. A category is a "
            + "claim about what the tool does; an empty one claims something and delivers nothing.");
    }

    /// <summary>
    /// The category is a claim, and the docs page is where the claim gets defended. Naming the
    /// category there forces the author to notice which one they picked.
    /// </summary>
    [Theory]
    [MemberData(nameof(TweakIds))]
    public void Every_docs_page_names_the_category_it_claims(string id)
    {
        var tweak = All.Single(t => t.Metadata.Id == id);
        var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", "tweaks", $"{id}.md"));

        Assert.True(
            text.Contains(tweak.Metadata.CategoryInfo.Name, StringComparison.OrdinalIgnoreCase),
            $"docs/tweaks/{id}.md does not mention '{tweak.Metadata.CategoryInfo.Name}', the "
            + "category it is filed under. Say what it improves, or file it somewhere else.");
    }

    /// <summary>The three profiles this build ships, loaded from the repository.</summary>
    private static IReadOnlyList<TweakProfile> Shipped { get; } =
        ProfileLoader.LoadDirectory(Path.Combine(RepoRoot, "profiles"));

    [Fact]
    public void Every_profile_names_tweaks_that_exist()
    {
        // A profile is a list of ids with nothing checking them. Rename a tweak and the profile
        // silently applies one fewer thing than it says on the card, which is the kind of
        // wrong nobody notices because the apply still reports success.
        var known = All.Select(t => t.Metadata.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = Shipped
            .SelectMany(p => p.Tweaks.Select(t => $"{p.Name}: {t.TweakId}"))
            .Where(pair => !known.Contains(pair.Split(": ")[1]))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void Every_option_a_profile_picks_exists_on_the_tweak()
    {
        var byId = All.ToDictionary(t => t.Metadata.Id, t => t.Metadata, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        foreach (var profile in Shipped)
        {
            foreach (var selection in profile.Tweaks)
            {
                if (!byId.TryGetValue(selection.TweakId, out var metadata))
                    continue;

                foreach (var (choiceId, optionId) in selection.EffectiveOptions)
                {
                    var choice = metadata.Choices.FirstOrDefault(c =>
                        string.Equals(c.Id, choiceId, StringComparison.OrdinalIgnoreCase));

                    if (choice is null)
                        problems.Add($"{profile.Name}/{selection.TweakId}: no choice '{choiceId}'");
                    else if (!choice.Options.Any(o => string.Equals(o.Id, optionId, StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"{profile.Name}/{selection.TweakId}/{choiceId}: no option '{optionId}'");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// No profile applies anything rated Risky or Experimental.
    ///
    /// A profile is one click, and one click must not be able to leave a machine unbootable or
    /// without a display. Both of those tweaks stay in the catalog with their own warnings and
    /// their own docs page, where somebody choosing them has read what they do.
    /// </summary>
    [Fact]
    public void No_profile_applies_a_risky_or_experimental_tweak()
    {
        var byId = All.ToDictionary(t => t.Metadata.Id, t => t.Metadata, StringComparer.OrdinalIgnoreCase);

        var offenders = Shipped
            .SelectMany(p => p.Tweaks.Select(t => (Profile: p.Name, t.TweakId)))
            .Where(x => byId.TryGetValue(x.TweakId, out var m)
                        && m.Risk is Risk.Risky or Risk.Experimental)
            .Select(x => $"{x.Profile}: {x.TweakId}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Basic, Intermediate and Expert are a ladder, and each rung contains the one below it.
    ///
    /// That is the whole claim the names make. If Intermediate could drop something Basic
    /// applies, "everything in Basic, plus..." would be a lie, and somebody moving up a rung
    /// would silently have a change reverted out from under them.
    /// </summary>
    [Theory]
    [InlineData("basic", "intermediate")]
    [InlineData("intermediate", "expert")]
    public void Each_profile_contains_the_one_below_it(string lower, string higher)
    {
        var below = Shipped.Single(p => p.Name == lower);
        var above = Shipped.Single(p => p.Name == higher);

        var missing = below.Tweaks
            .Select(t => t.TweakId)
            .Except(above.Tweaks.Select(t => t.TweakId), StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"'{higher}' says it contains everything in '{lower}' and is missing: "
            + string.Join(", ", missing));

        Assert.True(above.Tweaks.Count > below.Tweaks.Count, $"'{higher}' adds nothing to '{lower}'.");
    }

    /// <summary>
    /// The Gaming/Windows split is the promise the catalog makes before any individual tweak
    /// does. An empty half would mean the tool claims a distinction it does not draw.
    /// </summary>
    [Fact]
    public void Both_halves_of_the_catalog_have_tweaks_in_them()
    {
        foreach (var group in Enum.GetValues<TweakGroup>())
        {
            Assert.True(
                All.Any(t => TweakCategories.GroupOf(t.Metadata.Category) == group),
                $"No tweak is filed under {group}.");
        }
    }

    /// <summary>
    /// Filing under Gaming is a claim that the mechanism reaches the game. Saying so on the
    /// page is what stops a service tweak drifting into that half because it felt tidier.
    /// </summary>
    [Theory]
    [MemberData(nameof(TweakIds))]
    public void Every_docs_page_names_the_group_it_is_filed_under(string id)
    {
        var tweak = All.Single(t => t.Metadata.Id == id);
        var group = TweakCategories.GroupOf(tweak.Metadata.Category);
        var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", "tweaks", $"{id}.md"));

        Assert.True(
            text.Contains($"**Group:** {TweakCategories.NameOfGroup(group)}", StringComparison.Ordinal),
            $"docs/tweaks/{id}.md does not declare **Group:** {TweakCategories.NameOfGroup(group)}.");
    }

    [Fact]
    public void Category_ids_are_lowercase_slugs_and_unique()
    {
        foreach (var category in TweakCategories.All)
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", category.Id);
            Assert.False(string.IsNullOrWhiteSpace(category.Name), category.Id);
            Assert.False(string.IsNullOrWhiteSpace(category.Promise), category.Id);
        }

        Assert.Equal(
            TweakCategories.All.Count,
            TweakCategories.All.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Equal(
            TweakCategories.All.Count,
            TweakCategories.All.Select(c => c.Order).Distinct().Count());
    }

    [Fact]
    public void An_unknown_category_is_rejected_rather_than_becoming_a_new_bucket()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new TweakMetadata
            {
                Id = "x.y",
                Title = "t",
                Summary = "s",
                Category = "cpu",
                Scope = TweakScope.Machine,
                Lifetime = TweakLifetime.Persistent,
                Risk = Risk.Safe,
                Evidence = Evidence.Plausible,
            });

        Assert.Contains("Unknown tweak category", thrown.Message);
    }

    [Fact]
    public void Machine_scoped_tweaks_require_elevation()
    {
        foreach (var tweak in All.Where(t => t.Metadata.Scope == TweakScope.Machine))
            Assert.True(tweak.Metadata.RequiresElevation, tweak.Metadata.Id);
    }

    [Fact]
    public void Session_only_tweaks_never_require_a_reboot()
    {
        // A change that evaporates on reboot cannot also need one to take effect.
        foreach (var tweak in All.Where(t => t.Metadata.Lifetime == TweakLifetime.SessionOnly))
            Assert.False(tweak.Metadata.RequiresReboot, tweak.Metadata.Id);
    }

    [Fact]
    public void Conflicts_reference_tweaks_that_exist()
    {
        var ids = All.Select(t => t.Metadata.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tweak in All)
        {
            foreach (var other in tweak.Metadata.ConflictsWith)
                Assert.True(ids.Contains(other), $"{tweak.Metadata.Id} conflicts with unknown tweak '{other}'");
        }
    }

    [Fact]
    public void Declarative_entries_parse_and_target_supported_hives()
    {
        var supported = new[] { "HKLM", "HKCU", "HKCR", "HKU" };

        foreach (var definition in RegistryTweakCatalog.LoadEmbedded())
        {
            // A tweak writes values directly, or gets them from a choice, but it has to write
            // something: an entry that changes nothing under any selection is a bug, not a
            // no-op the user asked for.
            Assert.NotEmpty(definition.AllReachableValues);

            foreach (var value in definition.AllReachableValues)
            {
                Assert.Contains(value.Hive.ToUpperInvariant(), supported);
                Assert.False(string.IsNullOrWhiteSpace(value.Key), definition.Id);
            }
        }
    }

    /// <summary>
    /// A tweak Home cannot use has to say so where somebody on Home would read it.
    ///
    /// The window already reports it as not applicable, with the reason. The docs page is the
    /// other half: a reader who wants to know *why* their machine will not take a setting that
    /// clearly exists in the registry should find the answer on the page for it, not conclude
    /// the program is guessing.
    /// </summary>
    [Fact]
    public void Every_pro_only_tweak_documents_the_edition_it_needs()
    {
        foreach (var definition in RegistryTweakCatalog.LoadEmbedded().Where(d => d.ProOnly))
        {
            var text = File.ReadAllText(
                Path.Combine(RepoRoot, "docs", "tweaks", $"{definition.Id}.md"));

            Assert.True(
                text.Contains("Home", StringComparison.Ordinal),
                $"docs/tweaks/{definition.Id}.md is marked proOnly but never mentions Home, "
                + "the edition it will refuse to run on.");
        }
    }

    [Fact]
    public void Choices_are_internally_consistent()
    {
        foreach (var definition in RegistryTweakCatalog.LoadEmbedded())
        {
            foreach (var choice in definition.Choices)
            {
                var where = $"{definition.Id}/{choice.Id}";

                // Fewer than two options is not a choice, it is a value with extra ceremony.
                Assert.True(choice.Options.Count >= 2, $"{where} has {choice.Options.Count} option(s)");

                var ids = choice.Options.Select(o => o.Id).ToList();
                Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

                // The default has to exist, or every apply throws the moment nobody picks.
                Assert.Contains(choice.Default, ids, StringComparer.OrdinalIgnoreCase);

                // At most one recommendation: two is not a recommendation.
                Assert.True(choice.Options.Count(o => o.Recommended) <= 1, $"{where} recommends more than one option");

                foreach (var option in choice.Options)
                {
                    Assert.False(string.IsNullOrWhiteSpace(option.Title), $"{where}/{option.Id}");

                    // The whole point of a choice is that the user is told what each one costs.
                    // An option with a token description is worse than no choice at all.
                    Assert.True(
                        option.Description.Length >= 40,
                        $"{where}/{option.Id} needs a real description of what it does, got: '{option.Description}'");
                }
            }
        }
    }

    [Fact]
    public void Every_choice_option_resolves_to_the_values_it_declares()
    {
        foreach (var definition in RegistryTweakCatalog.LoadEmbedded())
        {
            foreach (var choice in definition.Choices)
            {
                foreach (var option in choice.Options)
                {
                    var selected = definition.ValuesFor(
                        new Dictionary<string, string> { [choice.Id] = option.Id });

                    foreach (var expected in option.Values)
                    {
                        Assert.Contains(selected, v =>
                            v.Name == expected.Name && v.Value == expected.Value && v.Key == expected.Key);
                    }
                }
            }
        }
    }

    [Fact]
    public void User_scoped_registry_tweaks_only_touch_the_user_hive()
    {
        // A "User" tweak that writes to HKLM would silently need elevation and would apply to
        // everyone on the machine.
        foreach (var definition in RegistryTweakCatalog.LoadEmbedded()
                     .Where(d => d.Scope == TweakScope.User))
        {
            foreach (var value in definition.AllReachableValues)
                Assert.Equal("HKCU", value.Hive.ToUpperInvariant());
        }
    }

    [Fact]
    public void Reboot_requiring_tweaks_are_at_least_moderate_risk()
    {
        // If it needs a reboot, a bad outcome is not visible until the machine comes back, and
        // by then the user cannot read the page that warned them. Nothing auto-reverts, so the
        // rating is the warning.
        foreach (var tweak in All.Where(t => t.Metadata.RequiresReboot))
            Assert.True(tweak.Metadata.Risk >= Risk.Moderate, tweak.Metadata.Id);
    }
}
