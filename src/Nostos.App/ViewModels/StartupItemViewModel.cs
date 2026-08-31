using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nostos.Core.Localization;
using Nostos.Ipc;
using Nostos.Win32.Services;

namespace Nostos.App.ViewModels;

/// <summary>
/// One row of the startup list: a program, whether it runs at sign-in, and its icon.
/// </summary>
public sealed class StartupItemViewModel : ObservableObject
{
    private readonly Func<StartupItemViewModel, bool, Task> _toggle;
    private StartupEntry _entry;
    private bool _isBusy;

    public StartupItemViewModel(StartupEntry entry, Func<StartupItemViewModel, bool, Task> toggle)
    {
        _entry = entry;
        _toggle = toggle;
        Icon = LoadIcon(entry.ExecutablePath);
    }

    public string Id => _entry.Id;

    /// <summary>
    /// The publisher's own name for the program where the registry has one worth showing.
    ///
    /// A Run value's name is chosen by whoever wrote the installer, so it ranges from "Steam"
    /// to "RtkAudUService" to a GUID. It is still the best short label available -- the
    /// alternative, the file name, is frequently worse ("Update.exe" for Discord) -- so the row
    /// shows the name and puts the path underneath, where a name that explains nothing can be
    /// resolved by looking at where it came from.
    /// </summary>
    public string Name => _entry.Name;

    /// <summary>Where the entry lives, e.g. <c>HKCU\...\CurrentVersion\Run</c>.</summary>
    public string Location => _entry.Location;

    /// <summary>The resolved executable, or the raw command when it could not be resolved.</summary>
    public string Path => _entry.ExecutablePath ?? _entry.Command;

    public bool IsEnabled => _entry.IsEnabled;

    public bool IsMachineWide => _entry.IsMachineWide;

    /// <summary>
    /// Dims a row that will not run.
    ///
    /// The list's whole job is answering "what starts with my PC", and the first version drew
    /// the on and the off rows identically apart from a small pill at the far right -- so the
    /// answer took reading fifteen rows edge to edge instead of one glance down the column.
    /// Dimming is doing the work here; the pill only confirms it.
    /// </summary>
    public double RowOpacity => _entry.IsEnabled ? 1.0 : 0.45;

    /// <summary>
    /// The two halves of the switch, crossfaded rather than swapped.
    ///
    /// Both states are always in the tree and one of them is transparent, so flipping a row
    /// animates instead of blinking. That is the whole point of the affordance: a control that
    /// visibly moves when clicked is one people believe they can click, and the first version --
    /// a word that silently changed from "On" to "Off" -- was not.
    /// </summary>
    public double OnOpacity => _entry.IsEnabled ? 1 : 0;

    public double OffOpacity => _entry.IsEnabled ? 0 : 1;

    /// <summary>
    /// What clicking the row will do, as a sentence.
    ///
    /// On the tooltip rather than the row, because it is the same sentence fifteen times and
    /// printing it on every line would bury the program names it is there to help with.
    /// </summary>
    public string ToggleHint => Strings.Format(
        _entry.IsEnabled ? "startup.hint.turnoff" : "startup.hint.turnon", _entry.Name);

    /// <summary>The scope badge: whether switching this affects everyone or only this account.</summary>
    public string ScopeText => Strings.Get(_entry.IsMachineWide ? "startup.allusers" : "startup.you");

    /// <summary>Greys the row while its write is in flight, so it cannot be clicked twice.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                Raise(nameof(IsInteractive));
        }
    }

    public bool IsInteractive => !_isBusy;

    /// <summary>
    /// The program's icon, or null when it has none.
    ///
    /// Read once when the row is built rather than bound lazily. Fifteen icons is fifteen shell
    /// calls, which is not worth the machinery of loading them in the background; a machine with
    /// enough startup entries for that to matter has a bigger problem than this list's latency.
    /// </summary>
    public Bitmap? Icon { get; }

    public bool HasIcon => Icon is not null;

    public async Task ToggleAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await _toggle(this, !IsEnabled).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Takes the state the machine actually reports after a write, not what was asked for.</summary>
    public void Update(StartupEntry entry)
    {
        _entry = entry;
        Raise(nameof(IsEnabled));
        Raise(nameof(RowOpacity));
        Raise(nameof(OnOpacity));
        Raise(nameof(OffOpacity));
        Raise(nameof(ToggleHint));
        Raise(nameof(Path));
        Raise(nameof(Location));
    }

    public void RefreshText()
    {
        Raise(nameof(ScopeText));
        Raise(nameof(ToggleHint));
    }

    /// <summary>
    /// Turns the pixels the Win32 layer read into something Avalonia can draw.
    ///
    /// <see cref="WriteableBitmap"/> rather than decoding a file, because the source is an icon
    /// resource inside an executable and there is no file to decode. The pixels arrive top-down
    /// BGRA with straight alpha, which is what is declared here -- getting the alpha format
    /// wrong produces icons with dark haloes that look like bad anti-aliasing rather than a bug.
    /// </summary>
    private static Bitmap? LoadIcon(string? path)
    {
        try
        {
            if (FileIcons.TryRead(path) is not { } icon)
                return null;

            var bitmap = new WriteableBitmap(
                new PixelSize(icon.Width, icon.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (var buffer = bitmap.Lock())
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    icon.Bgra, 0, buffer.Address, icon.Bgra.Length);
            }

            return bitmap;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // An icon is decoration. Nothing about this list stops working without one.
            return null;
        }
    }
}
