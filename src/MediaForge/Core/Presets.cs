namespace MediaForge.Core;

public sealed record AudioFormat(string Id, string Label, string Ext, string Codec, bool Lossy)
{
    public bool IsCopy => Id == "copy";

    public static readonly AudioFormat[] All =
    [
        new("mp3", "MP3", "mp3", "libmp3lame", true),
        new("m4a", "M4A (AAC)", "m4a", "aac", true),
        new("opus", "Opus", "opus", "libopus", true),
        new("ogg", "OGG (Vorbis)", "ogg", "libvorbis", true),
        new("wma", "WMA", "wma", "wmav2", true),
        new("flac", "FLAC", "flac", "flac", false),
        new("wav", "WAV", "wav", "pcm_s16le", false),
        new("alac", "ALAC (M4A)", "m4a", "alac", false),
        new("aiff", "AIFF", "aiff", "pcm_s16be", false),
        // Stream copy: extension is chosen from the source codec at run time.
        new("copy", "Fmt_AudioCopy", "", "copy", false),
    ];
}

public enum VCodec { H264, H265, VP9, AV1, MPEG4, Copy }

public sealed record VideoFormat(string Id, string Label, string Ext, VCodec Codec, string AudioCodec)
{
    public bool IsRemux => Codec == VCodec.Copy;
    public bool SupportsHardware => Codec is VCodec.H264 or VCodec.H265;

    public static readonly VideoFormat[] All =
    [
        new("mp4_h264", "MP4 (H.264)", "mp4", VCodec.H264, "aac"),
        new("mp4_h265", "MP4 (H.265 / HEVC)", "mp4", VCodec.H265, "aac"),
        new("mp4_av1", "MP4 (AV1)", "mp4", VCodec.AV1, "aac"),
        new("mkv_h264", "MKV (H.264)", "mkv", VCodec.H264, "aac"),
        new("mkv_h265", "MKV (H.265 / HEVC)", "mkv", VCodec.H265, "aac"),
        new("webm_vp9", "WebM (VP9)", "webm", VCodec.VP9, "libopus"),
        new("webm_av1", "WebM (AV1)", "webm", VCodec.AV1, "libopus"),
        new("mov_h264", "MOV (H.264)", "mov", VCodec.H264, "aac"),
        new("avi_mpeg4", "AVI (MPEG-4)", "avi", VCodec.MPEG4, "libmp3lame"),
        new("remux_mp4", "Fmt_RemuxMp4", "mp4", VCodec.Copy, "copy"),
        new("remux_mkv", "Fmt_RemuxMkv", "mkv", VCodec.Copy, "copy"),
    ];
}

public enum Quality { VeryHigh, High, Medium, Low }

public enum EncodeSpeed { Fast, Balanced, Small }

public sealed record AudioOptions(AudioFormat Format, int Bitrate, int SampleRate, int Channels);

public sealed record VideoOptions(
    VideoFormat Format,
    HwEncoder Encoder,
    Quality Quality,
    EncodeSpeed Speed,
    int MaxHeight,
    int Fps,
    int AudioBitrate,
    bool RemoveAudio);

public sealed record TagSet(
    string Title, string Artist, string Album, string AlbumArtist,
    string Date, string Track, string Genre, string Comment)
{
    public IEnumerable<(string Key, string Value)> Pairs =>
    [
        ("title", Title), ("artist", Artist), ("album", Album), ("album_artist", AlbumArtist),
        ("date", Date), ("track", Track), ("genre", Genre), ("comment", Comment),
    ];
}

public enum CoverAction { Keep, Replace, Remove }
