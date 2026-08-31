using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nostos.Core.Localization;

/// <summary>The languages the interface is available in.</summary>
public enum Language
{
    /// <summary>The default, and the language every string is authored in.</summary>
    English,

    German,
}

/// <summary>
/// The user interface's text, in the language the user picked.
///
/// A table of keys rather than literals in the views, so that a second language is a second
/// data file instead of a second copy of the program. English is not just one of the two
/// tables: it is the fallback for every key, so a German string that has not been written yet
/// shows the English one rather than a blank or a crash. That is what makes it safe to add a
/// string to the app without translating it in the same commit.
///
/// The tables are embedded in the assembly, not read from disk. A single-file build has no
/// folder of .json files beside it, and a language that could be edited on disk would be a way
/// to change what the confirmation before a destructive action says.
/// </summary>
public sealed class Strings : INotifyPropertyChanged
{
    /// <summary>The one instance, for anything that needs an object rather than a static.</summary>
    public static Strings Instance { get; } = new();

    private static readonly Dictionary<Language, IReadOnlyDictionary<string, string>> Tables = [];

    private static Language _language = Language.English;
    private static int _revision;

    private Strings()
    {
    }

    /// <summary>
    /// Bumped every time the language changes.
    ///
    /// Nothing binds to it any more -- the views bind to an observable instead -- but it is
    /// what makes this object worth being an INotifyPropertyChanged at all, and it is a cheap
    /// way for anything that wants to know "has the language changed since I last looked".
    /// </summary>
    public int Revision => _revision;

    /// <summary>Raised after the language changes, for anything that is not a binding.</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The language the interface is currently in. English until something sets it.
    /// </summary>
    public static Language Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;

            _language = value;
            _revision++;

            // Without this the tables would swap and the window would carry on showing the
            // language it was opened in.
            Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(Revision)));

            LanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// The text for a key in the current language.
    ///
    /// Falls back to English, and then to the key itself. A key rendered on screen is ugly and
    /// unmistakable, which is the point: a silently blank label is a bug that ships.
    /// </summary>
    public static string Get(string key)
    {
        if (Table(_language).TryGetValue(key, out var text))
            return text;

        if (_language != Language.English && Table(Language.English).TryGetValue(key, out var english))
            return english;

        return key;
    }

    /// <summary>
    /// The text for a key, with <c>{0}</c>, <c>{1}</c> and so on filled in.
    ///
    /// Invariant culture on purpose. These are counts and version numbers going into a
    /// sentence, and a German decimal comma in the middle of "0.1.0" helps nobody.
    /// </summary>
    public static string Format(string key, params object?[] arguments)
    {
        var format = Get(key);

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, arguments);
        }
        catch (FormatException)
        {
            // A translation with a placeholder the English string does not have would
            // otherwise take the window down. StringParityTests exists to stop that reaching a
            // build, but a running program should degrade to showing the raw text.
            return format;
        }
    }

    /// <summary>
    /// A counted noun: "1 change", "4 Änderungen".
    ///
    /// Two keys per noun, <c>.one</c> and <c>.many</c>, rather than a count and a suffix. The
    /// English plural of almost every noun is the singular plus an s, which makes a helper that
    /// appends one look like it works; German plurals are not formed that way and neither are
    /// most languages'. Writing both forms out is the only version of this that survives a
    /// second language.
    /// </summary>
    public static string Plural(string keyPrefix, int count)
        => Format(count == 1 ? $"{keyPrefix}.one" : $"{keyPrefix}.many", count);

    /// <summary>
    /// The text for a key, or <paramref name="fallback"/> when the table has no such key.
    ///
    /// For text that already exists in English somewhere else in the program: a category's
    /// name, a tweak's title. The English is the source of truth and lives with the thing it
    /// describes; this only asks whether a translation has been written for it.
    /// </summary>
    public static string GetOr(string key, string fallback)
    {
        if (Table(_language).TryGetValue(key, out var text))
            return text;

        return _language != Language.English && Table(Language.English).TryGetValue(key, out var english)
            ? english
            : fallback;
    }

    /// <summary>
    /// A translation for text that arrived already written in English, or that English back.
    ///
    /// Different from <see cref="GetOr"/> in one way that matters: it reads the current
    /// language's table and stops. It never falls through to the English table, because the
    /// caller is holding better English than the table has -- the profile file the user may
    /// have edited, the sentence the service actually sent. In English it therefore returns
    /// what it was given, which is the same rule the catalog translations follow: English is
    /// the language the data is written in, so in English the data wins.
    /// </summary>
    public static string Translate(string key, string english)
        => Table(_language).TryGetValue(key, out var text) ? text : english;

    /// <summary>
    /// The same, with values substituted into it.
    ///
    /// For a tweak's "not applicable" reason, which is produced by a service running as SYSTEM
    /// that has no user and so cannot know what language to answer in. It sends the finished
    /// English and the key beside it; this picks. The English is already formatted, so the
    /// arguments are only ever applied to a translation.
    /// </summary>
    public static string Translate(string key, string english, IReadOnlyList<string>? arguments)
    {
        if (!Table(_language).TryGetValue(key, out var format))
            return english;

        if (arguments is null || arguments.Count == 0)
            return format;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, [.. arguments]);
        }
        catch (FormatException)
        {
            // A translation with a placeholder the English does not have. StringTableTests
            // stops that reaching a build; a running program shows the English instead.
            return english;
        }
    }

    /// <summary>Every key in a language's table. For the tests that keep the tables in step.</summary>
    public static IReadOnlyCollection<string> Keys(Language language) => Table(language).Keys.ToList();

    /// <summary>
    /// A date written out: "Tuesday 25 August", "Dienstag, 25. August".
    ///
    /// Formatted from the string table rather than from a CultureInfo, because this program is
    /// built with InvariantGlobalization and there is no culture data to ask. Requesting one
    /// does not fall back politely; CultureInfo.GetCultureInfo("de-DE") throws.
    ///
    /// Keeping that build setting is worth more than the framework's date formatting: it is
    /// several megabytes of ICU in a download this project has deliberately shrunk, and the
    /// only culture-dependent text in the whole app is this one line on a journal row.
    ///
    /// The order is part of the translation, not a constant, which is the point of doing it
    /// this way at all: English writes "Tuesday 25 August" and German writes
    /// "Dienstag, 25. August".
    /// </summary>
    /// <param name="withYear">
    /// True for a date in another year, which is written without the weekday: knowing it was a
    /// Tuesday is no help once you no longer remember the week.
    /// </param>
    public static string DateText(DateTimeOffset when, bool withYear)
    {
        var month = Get($"date.month.{when.Month}");

        return withYear
            ? Format("date.withyear", when.Day, month, when.Year)
            : Format("date.withweekday", Get($"date.day.{(int)when.DayOfWeek}"), when.Day, month);
    }

    /// <summary>The two-letter code, for settings files and culture lookups.</summary>
    public static string CodeOf(Language language) => language switch
    {
        Language.German => "de",
        _ => "en",
    };

    /// <summary>
    /// The language's name written in itself: "English", "Deutsch".
    ///
    /// A language picker that lists "German" is only useful to somebody who already reads
    /// English, which is exactly the person who does not need it.
    /// </summary>
    public static string NativeNameOf(Language language) => language switch
    {
        Language.German => "Deutsch",
        _ => "English",
    };

    /// <summary>
    /// The language that best matches a culture, or null if none does.
    ///
    /// Used once, to pick a default for somebody who has never opened the settings panel.
    /// </summary>
    public static Language? ForCulture(CultureInfo culture)
    {
        var name = culture.TwoLetterISOLanguageName;

        return string.Equals(name, "de", StringComparison.OrdinalIgnoreCase)
            ? Language.German
            : null;
    }

    private static IReadOnlyDictionary<string, string> Table(Language language)
    {
        if (Tables.TryGetValue(language, out var table))
            return table;

        table = Load(language);
        Tables[language] = table;
        return table;
    }

    private static IReadOnlyDictionary<string, string> Load(Language language)
    {
        var code = CodeOf(language);
        var assembly = typeof(Strings).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".{code}.json", StringComparison.OrdinalIgnoreCase));

        if (name is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var parsed = JsonSerializer.Deserialize(
            stream, StringsJsonContext.Default.DictionaryStringString);

        return parsed is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }
}

/// <summary>
/// The string table format: a flat map of key to text.
///
/// Flat rather than nested by screen. A nested file reads more tidily and makes it harder to
/// answer the only question that matters when translating, which is whether the two files hold
/// the same set of keys.
/// </summary>
[JsonSourceGenerationOptions(
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class StringsJsonContext : JsonSerializerContext;
