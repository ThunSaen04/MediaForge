using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace MediaForge.Localization;

/// <summary>Runtime-switchable string table. XAML binds through <see cref="TrExtension"/>.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changes, for view models that format text in code.</summary>
    public event Action? LanguageChanged;

    public string Language
    {
        get;
        set
        {
            var normalized = value == "en" ? "en" : "th";
            if (field == normalized) return;
            field = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke();
        }
    } = "th";

    public string this[string key] => Strings.Get(key, Language);

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, Instance[key], args);
}

/// <summary>Usage: <c>Text="{l:Tr Key}"</c> — updates live when the language changes.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>A combo box choice whose label follows the current language.</summary>
public sealed class Option<T> : INotifyPropertyChanged
{
    private readonly string _label;
    private readonly bool _localize;

    public Option(string label, T value, bool localize = true)
    {
        _label = label;
        _localize = localize;
        Value = value;
        if (localize)
            Loc.Instance.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public T Value { get; }

    public string Label => _localize ? Loc.T(_label) : _label;

    public override string ToString() => Label;
}
