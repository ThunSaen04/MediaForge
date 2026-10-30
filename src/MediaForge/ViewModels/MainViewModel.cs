using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class MainViewModel : Observable
{
    private readonly AppSettings _settings;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        Queue = new QueueViewModel(settings);
        Convert = new ConvertViewModel(Queue, settings);
        Edit = new EditViewModel(Queue);
        YouTube = new YouTubeViewModel(Queue, settings);
        Metadata = new MetadataViewModel(Queue);
        Settings = new SettingsViewModel(settings, Queue);
        Update = new UpdateViewModel(settings, Queue);

        Languages = [new("ไทย", "th", false), new("English", "en", false)];
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == settings.Language) ?? Languages[0];
        Themes = [new("Theme_System", "System"), new("Theme_Light", "Light"), new("Theme_Dark", "Dark")];
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == settings.Theme) ?? Themes[0];
    }

    public QueueViewModel Queue { get; }
    public ConvertViewModel Convert { get; }
    public EditViewModel Edit { get; }
    public YouTubeViewModel YouTube { get; }
    public MetadataViewModel Metadata { get; }
    public SettingsViewModel Settings { get; }
    public UpdateViewModel Update { get; }

    public List<Option<string>> Languages { get; }

    public Option<string> SelectedLanguage
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            Loc.Instance.Language = value.Value;
            _settings.Language = value.Value;
            _settings.Save();
        }
    } = null!;

    public List<Option<string>> Themes { get; }

    public Option<string> SelectedTheme
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            ThemeHelper.Apply(value.Value);
            _settings.Theme = value.Value;
            _settings.Save();
        }
    } = null!;
}
