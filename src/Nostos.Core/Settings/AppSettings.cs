using System.Text.Json;
using System.Text.Json.Serialization;
using Nostos.Core.Localization;

namespace Nostos.Core.Settings;

/// <summary>How often the app asks GitHub whether there is a newer release.</summary>
public enum UpdateCadence
{
    /// <summary>Once per launch. The default, and cheap: one request against a 60/hour budget.</summary>
    EveryLaunch,

    /// <summary>At most once a day, however often the app is opened.</summary>
    Daily,

    /// <summary>At most once a week.</summary>
    Weekly,
}

/// <summary>
/// The handful of things the user gets to decide about the app itself, as opposed to about
/// their machine.
///
/// Deliberately small, and deliberately not a settings framework. Everything else this program
/// does is a tweak with a documented effect and a revert; a preferences file that grew every
/// time a decision was awkward would be the place all of that discipline went to die.
///
/// Lives beside the journal so that a portable copy carries its preferences with it, and an
/// installed one keeps them where both halves of the app can read them.
/// </summary>
public sealed record AppSettings
{
    public static string Path => AppPaths.SettingsPath;

    /// <summary>
    /// Whether to check for updates at all. Null means the file does not say.
    ///
    /// Nullable for the reason CoreJson spells out at length: the source generator builds this
    /// record with an object initializer and passes `default` for every property the JSON does
    /// not mention, so a plain `= true` initializer NEVER RUNS during a load. It comes back
    /// false, and an older settings file would silently switch update checking off. Null is a
    /// third state the generator cannot fake, so absence stays distinguishable from a choice.
    ///
    /// Read it through <see cref="UpdateChecksEnabled"/>, which applies the default.
    /// </summary>
    public bool? CheckForUpdates { get; init; }

    /// <summary>
    /// Whether to check, with the default applied.
    ///
    /// On unless the user said otherwise. An optimizer that rewrites machine state is a program
    /// whose bugs matter, and a copy that never learns a fix exists is the worst version of this
    /// to be running. The check is a single unauthenticated GET that reads a version number;
    /// nothing is downloaded, and nothing is installed without a click.
    /// </summary>
    [JsonIgnore]
    public bool UpdateChecksEnabled => CheckForUpdates ?? true;

    public UpdateCadence Cadence { get; init; } = UpdateCadence.EveryLaunch;

    /// <summary>
    /// The language the interface is in. Null means the file does not say.
    ///
    /// Nullable for the same reason as <see cref="CheckForUpdates"/>: a property initializer on
    /// this record never runs during a source-generated load, so a plain default would come
    /// back as whichever enum member happens to be zero rather than as "not chosen". Here that
    /// would be indistinguishable from a deliberate choice of English, and the difference
    /// matters exactly once, on a machine that has never opened the settings panel.
    ///
    /// Read it through <see cref="InterfaceLanguage"/>.
    /// </summary>
    public Language? Language { get; init; }

    /// <summary>
    /// The language to use, with the default applied.
    ///
    /// English unless the user chose otherwise. Deliberately not taken from the operating
    /// system's language: this program is documented in English, its tweak ids and its docs
    /// pages are in English, and a German Windows install is not on its own a statement that
    /// somebody wants a half-English window. Picking German is one click, and it is
    /// remembered.
    /// </summary>
    [JsonIgnore]
    public Language InterfaceLanguage => Language ?? Localization.Language.English;

    /// <summary>When the last check completed, successful or not. Null means never.</summary>
    public DateTimeOffset? LastCheckedUtc { get; init; }

    /// <summary>How long <see cref="Cadence"/> asks us to wait between checks.</summary>
    [JsonIgnore]
    public TimeSpan Interval => Cadence switch
    {
        UpdateCadence.Daily => TimeSpan.FromDays(1),
        UpdateCadence.Weekly => TimeSpan.FromDays(7),
        _ => TimeSpan.Zero,
    };

    /// <summary>
    /// Whether an automatic check is due.
    ///
    /// A failed check still counts as a check. Otherwise a machine that is offline for a week
    /// would ask on every single launch, which is exactly the machine least able to answer and
    /// exactly the user least interested in being asked.
    /// </summary>
    public bool IsCheckDue(DateTimeOffset now)
    {
        if (!UpdateChecksEnabled)
            return false;

        if (LastCheckedUtc is not { } last)
            return true;

        // A stamp in the future means the clock moved backwards -- a timezone fix, a dead CMOS
        // battery, a VM restored from a snapshot. Waiting for real time to catch up would
        // silently disable checking for as long as the skew lasts, so treat it as due.
        return last > now || now - last >= Interval;
    }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize(File.ReadAllText(Path), SettingsJsonContext.Default.AppSettings)
                  ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A settings file we cannot read is not worth failing a launch over. The defaults
            // are the same ones a first-time user gets.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes the file, atomically.
    /// </summary>
    /// <returns>
    /// False when it could not be saved. Returned rather than thrown because the caller is a
    /// checkbox: the honest response to "we could not remember that" is to say so in the panel
    /// the user is looking at, not to take down the window.
    /// </returns>
    public bool Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, Path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// Where the settings come from and go to.
///
/// An interface for one file with two methods, which needs a word of justification. The view
/// model that owns these preferences is the same one that owns "remove Nostos from this PC",
/// and a test of that has no business reading -- let alone writing -- the real user's file
/// under %ProgramData% on whatever machine CI is running on.
/// </summary>
public interface ISettingsStore
{
    AppSettings Load();

    /// <returns>False when the settings could not be written.</returns>
    bool Save(AppSettings settings);
}

/// <summary>The real one: <see cref="AppSettings.Path"/>.</summary>
public sealed class FileSettingsStore : ISettingsStore
{
    public AppSettings Load() => AppSettings.Load();

    public bool Save(AppSettings settings) => settings.Save();
}

/// <summary>
/// Written indented and with enums as names: this is a file somebody may open in Notepad to
/// find out why their copy stopped asking about updates, and "cadence": 2 would not tell them.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
