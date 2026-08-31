using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Nostos.Core.Localization;

namespace Nostos.App.Localization;

/// <summary>
/// Puts a translated string into a view: <c>Text="{loc:Tr app.refresh}"</c>.
///
/// It binds the target property to an observable rather than returning the text, because a
/// value returned once is a value that keeps the language the window was opened in.
///
/// The route matters. The obvious implementation returns <c>new Binding(...)</c> with a source
/// and a path, and that version cannot be published: a reflection binding is marked
/// RequiresDynamicCode, and the one-file download is an ahead-of-time build, so the publish
/// fails outright. Binding to an <see cref="IObservable{T}"/> reaches the same place with no
/// reflection, no expression trees and nothing for the trimmer to remove.
///
/// A compiled binding to an indexer, <c>{Binding Loc[app.refresh]}</c>, was tried first and is
/// the tidier-looking answer. It renders correctly and never updates: Avalonia's compiled
/// bindings do not re-read an indexer on the "Item[]" notification that means "every indexed
/// value changed". Measured, not assumed -- the button stayed in English next to one that
/// switched.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    /// <summary>The key to look up, for example <c>settings.updates</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is AvaloniaObject owner
            && target.TargetProperty is AvaloniaProperty property)
        {
            owner.Bind(property, new LocalizedText(Key), BindingPriority.LocalValue);
        }

        // Also the value the loader assigns, so the first frame is correct without waiting for
        // the observable, and so the extension still works somewhere it cannot reach a target.
        return Strings.Get(Key);
    }

    /// <summary>
    /// One key's text, pushed again every time the language changes.
    ///
    /// Hand-written rather than pulled from a reactive library: this is the only observable in
    /// the program, it has one subscriber, and the alternative is a dependency for thirty
    /// lines.
    /// </summary>
    private sealed class LocalizedText(string key) : IObservable<object?>
    {
        public IDisposable Subscribe(IObserver<object?> observer)
        {
            observer.OnNext(Strings.Get(key));

            void OnLanguageChanged() => observer.OnNext(Strings.Get(key));

            Strings.LanguageChanged += OnLanguageChanged;
            return new Unsubscriber(() => Strings.LanguageChanged -= OnLanguageChanged);
        }
    }

    /// <summary>
    /// Detaches the handler when the binding ends.
    ///
    /// Not optional: several of these live inside item templates, which are created and thrown
    /// away as a list scrolls. Without this, every row that ever existed would stay alive
    /// hanging off a static event.
    /// </summary>
    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _dispose, null);
            action?.Invoke();
        }
    }
}
