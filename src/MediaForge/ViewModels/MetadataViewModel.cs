using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class MetadataViewModel : Observable
{
    private readonly QueueViewModel _queue;
    private MediaInfo? _info;
    private CoverAction _coverAction = CoverAction.Keep;
    private string? _newCoverPath;

    public MetadataViewModel(QueueViewModel queue)
    {
        _queue = queue;
        SubtitleFormats = [new("SRT", "srt", false), new("ASS", "ass", false), new("WebVTT", "vtt", false)];
        SubtitleFormat = SubtitleFormats[0];

        ReloadCommand = new AsyncCommand(LoadAsync);
        SaveTagsCommand = new AsyncCommand(SaveTagsAsync, () => _info is not null);
        ChooseCoverCommand = new RelayCommand(ChooseCover, () => CanEditCover);
        RemoveCoverCommand = new RelayCommand(RemoveCover, () => CanEditCover);
        ExportCoverCommand = new AsyncCommand(ExportCoverAsync, () => _info?.Cover is not null);
        ExtractSubtitleCommand = new AsyncCommand(() => Enqueue(ExtractSubtitleJob()), () => SelectedSubtitle is not null);
        EmbedSubtitleCommand = new AsyncCommand(() => Enqueue(EmbedSubtitleJob()), () => _info?.HasVideo == true);
        BurnFileCommand = new AsyncCommand(() => Enqueue(BurnJob(fromFile: true)), () => _info?.HasVideo == true);
        BurnTrackCommand = new AsyncCommand(() => Enqueue(BurnJob(fromFile: false)),
            () => _info?.HasVideo == true && SelectedSubtitle is not null);
    }

    public string Input
    {
        get;
        set
        {
            if (Set(ref field, value)) _ = LoadAsync();
        }
    } = "";

    public string InfoText { get; private set => Set(ref field, value); } = "";
    public bool IsLoaded => _info is not null;

    public string TagTitle { get; set => Set(ref field, value); } = "";
    public string TagArtist { get; set => Set(ref field, value); } = "";
    public string TagAlbum { get; set => Set(ref field, value); } = "";
    public string TagAlbumArtist { get; set => Set(ref field, value); } = "";
    public string TagDate { get; set => Set(ref field, value); } = "";
    public string TagTrack { get; set => Set(ref field, value); } = "";
    public string TagGenre { get; set => Set(ref field, value); } = "";
    public string TagComment { get; set => Set(ref field, value); } = "";
    public bool OverwriteOriginal { get; set => Set(ref field, value); }

    public ImageSource? CoverImage { get; private set => Set(ref field, value); }
    public string CoverStatus { get; private set => Set(ref field, value); } = "";

    /// <summary>Cover art is supported for audio files in MP3, M4A and FLAC.</summary>
    public bool CanEditCover => _info is { HasVideo: false } && MediaTypes.SupportsCover(MediaTypes.Ext(_info.FilePath));

    public ObservableCollection<StreamInfo> Subtitles { get; } = [];
    public StreamInfo? SelectedSubtitle { get; set => Set(ref field, value); }
    public List<Option<string>> SubtitleFormats { get; }
    public Option<string> SubtitleFormat { get; set => Set(ref field, value); } = null!;
    public string SubtitleFile { get; set => Set(ref field, value); } = "";
    public string SubtitleLanguage { get; set => Set(ref field, value); } = "tha";

    public ICommand ReloadCommand { get; }
    public ICommand SaveTagsCommand { get; }
    public ICommand ChooseCoverCommand { get; }
    public ICommand RemoveCoverCommand { get; }
    public ICommand ExportCoverCommand { get; }
    public ICommand ExtractSubtitleCommand { get; }
    public ICommand EmbedSubtitleCommand { get; }
    public ICommand BurnFileCommand { get; }
    public ICommand BurnTrackCommand { get; }

    private async Task LoadAsync()
    {
        _info = null;
        _coverAction = CoverAction.Keep;
        _newCoverPath = null;
        InfoText = "";
        CoverImage = null;
        CoverStatus = "";
        Subtitles.Clear();
        NotifyLoaded();

        var path = Input;
        if (!File.Exists(path)) return;
        if (!await ToolGuard.EnsureFfmpegAsync()) return;

        MediaInfo info;
        try
        {
            info = await MediaProbe.RunAsync(path);
        }
        catch (Exception ex)
        {
            InfoText = ex.Message;
            return;
        }
        if (path != Input) return;

        _info = info;
        InfoText = info.Describe();
        TagTitle = info.Tag("title") ?? "";
        TagArtist = info.Tag("artist") ?? "";
        TagAlbum = info.Tag("album") ?? "";
        TagAlbumArtist = info.Tag("album_artist", "albumartist", "album artist") ?? "";
        TagDate = info.Tag("date", "year") ?? "";
        TagTrack = info.Tag("track", "tracknumber") ?? "";
        TagGenre = info.Tag("genre") ?? "";
        TagComment = info.Tag("comment", "description") ?? "";
        foreach (var s in info.Subtitles) Subtitles.Add(s);
        SelectedSubtitle = Subtitles.FirstOrDefault();
        NotifyLoaded();

        if (info.Cover is { } cover)
        {
            CoverStatus = Loc.T("Cover_Embedded");
            var temp = Path.Combine(AppPaths.TempDir, $"cover_{Guid.NewGuid():N}.{Converter.CoverExtension(cover)}");
            try
            {
                if (await Converter.ExportCoverAsync(path, temp)) CoverImage = LoadImage(temp);
            }
            catch
            {
                // Preview only; a broken cover should not block tag editing.
            }
            finally
            {
                Tools.TryDelete(temp);
            }
        }
        else
        {
            CoverStatus = CanEditCover ? Loc.T("Cover_None") : Loc.T("Cover_Unsupported");
        }
    }

    private void NotifyLoaded()
    {
        Notify(nameof(IsLoaded));
        Notify(nameof(CanEditCover));
        CommandManager.InvalidateRequerySuggested();
    }

    private static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // read fully so the temp file can be deleted
        image.DecodePixelWidth = 400;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void ChooseCover()
    {
        if (Dialogs.OpenFile(Dialogs.ImageFilter) is not { } file) return;
        _coverAction = CoverAction.Replace;
        _newCoverPath = file;
        CoverImage = LoadImage(file);
        CoverStatus = Loc.F("Cover_WillReplace", Path.GetFileName(file));
    }

    private void RemoveCover()
    {
        _coverAction = CoverAction.Remove;
        _newCoverPath = null;
        CoverImage = null;
        CoverStatus = Loc.T("Cover_WillRemove");
    }

    private async Task ExportCoverAsync()
    {
        if (_info?.Cover is not { } cover) return;
        var ext = Converter.CoverExtension(cover);
        if (Dialogs.SaveFile($"{ext.ToUpperInvariant()}|*.{ext}", $"{Path.GetFileNameWithoutExtension(_info.FilePath)}_cover.{ext}") is not { } dest)
            return;
        await Converter.ExportCoverAsync(_info.FilePath, dest);
        Dialogs.RevealInExplorer(dest);
    }

    private Task SaveTagsAsync()
    {
        if (_info is null) return Task.CompletedTask;
        var input = _info.FilePath;
        var tags = new TagSet(TagTitle, TagArtist, TagAlbum, TagAlbumArtist, TagDate, TagTrack, TagGenre, TagComment);
        var cover = CanEditCover ? _coverAction : CoverAction.Keep;
        var coverPath = _newCoverPath;
        var overwrite = OverwriteOriginal;

        var job = new Job(Path.GetFileName(input), Loc.T("Detail_Tags"), JobKind.Process,
            (j, ct) => Converter.WriteTagsAsync(input, tags, cover, coverPath, overwrite, j, ct));
        job.Succeeded += done =>
        {
            // Show the result: the rewritten original, or the new copy.
            if (done.OutputPath is { } output && File.Exists(output))
            {
                if (output == Input) _ = LoadAsync();
                else Input = output;
            }
        };
        return Enqueue(job);
    }

    private Job? ExtractSubtitleJob()
    {
        if (_info is null || SelectedSubtitle is not { } stream) return null;
        var input = _info.FilePath;
        var format = SubtitleFormat.Value;
        return new Job(Path.GetFileName(input), Loc.F("Detail_ExtractSub", stream.Index, format.ToUpperInvariant()), JobKind.Process,
            (j, ct) => Converter.ExtractSubtitleAsync(input, stream.Index, format, Path.GetDirectoryName(input)!, j, ct));
    }

    private Job? EmbedSubtitleJob()
    {
        if (_info is null) return null;
        if (!File.Exists(SubtitleFile))
        {
            Dialogs.Info(Loc.T("Msg_ChooseSubtitle"));
            return null;
        }
        var input = _info.FilePath;
        var sub = SubtitleFile;
        var language = SubtitleLanguage;
        return new Job(Path.GetFileName(input), Loc.F("Detail_EmbedSub", Path.GetFileName(sub)), JobKind.Process,
            (j, ct) => Converter.EmbedSubtitleAsync(input, sub, language, Path.GetDirectoryName(input)!, j, ct));
    }

    private Job? BurnJob(bool fromFile)
    {
        if (_info is null) return null;
        var input = _info.FilePath;
        string? sub = null;
        int? track = null;
        if (fromFile)
        {
            if (!File.Exists(SubtitleFile))
            {
                Dialogs.Info(Loc.T("Msg_ChooseSubtitle"));
                return null;
            }
            sub = SubtitleFile;
        }
        else
        {
            if (SelectedSubtitle is not { } stream) return null;
            track = stream.Index;
        }
        var detail = Loc.F("Detail_BurnSub", sub is null ? $"#{track}" : Path.GetFileName(sub));
        return new Job(Path.GetFileName(input), detail, JobKind.Process,
            (j, ct) => Converter.BurnSubtitleAsync(input, sub, track, Path.GetDirectoryName(input)!, j, ct));
    }

    private async Task Enqueue(Job? job)
    {
        if (job is null || !await ToolGuard.EnsureFfmpegAsync()) return;
        _queue.Add(job);
    }
}
