namespace VoiceBridge.Core;

public enum RecordEntryKind
{
    Timestamp = 0,
    Message = 1,
    SystemNotice = 2,
    TransitionalHint = 3
}

public enum FeedStageKind
{
    Ignored = 0,
    Received = 1,
    Transitional = 2,
    SystemNotice = 3
}

public readonly record struct FeedStageDecision(
    FeedStageKind Kind,
    string Text,
    bool IsAlert,
    TimeSpan AutoHideAfter);

/// <summary>
/// Pure classification and lifecycle rules for the WeChat-style message feed:
/// - Message events clear any previous transitional hint so only the latest
///   message ever shows transitional synthesis/playback status.
/// - Transitional hints ("正在合成…", "正在播放…", "已朗读") replace one another
///   in place and fade out automatically after a few seconds.
/// - Non-message status events render as quiet centered "拍一拍"-style notices.
/// </summary>
public static class MessageFeedPolicy
{
    public const int MaxVisibleItems = 60;
    public static readonly TimeSpan TimestampGap = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan SynthesizingHintLifetime = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan PlayedHintLifetime = TimeSpan.FromSeconds(2.5);

    public static bool ShouldInsertTimestamp(DateTimeOffset? lastHeaderAt, DateTimeOffset now) =>
        lastHeaderAt is null || now - lastHeaderAt.Value >= TimestampGap || now < lastHeaderAt.Value;

    public static string FormatTimestamp(DateTimeOffset timestamp) => timestamp.ToString("HH:mm");

    public static FeedStageDecision Classify(string stage) => stage switch
    {
        "文件通知 → 接收" => new(FeedStageKind.Ignored, "", false, TimeSpan.Zero),
        "查询请求 → 接收" => new(FeedStageKind.Ignored, "", false, TimeSpan.Zero),
        "已接收" => new(FeedStageKind.Received, "", false, TimeSpan.Zero),
        "收到首个有效音频帧" => new(FeedStageKind.Transitional, "正在合成…", false, SynthesizingHintLifetime),
        "已向音频设备提交播放" => new(FeedStageKind.Transitional, "正在播放…", false, SynthesizingHintLifetime),
        "播放完成" => new(FeedStageKind.Transitional, "已朗读", false, PlayedHintLifetime),
        "已清空待播队列" => new(FeedStageKind.SystemNotice, "已清空待播", false, TimeSpan.Zero),
        "跳过超长消息" => new(FeedStageKind.SystemNotice, "这条消息太长，已跳过", true, TimeSpan.Zero),
        "队列已满，跳过新消息" => new(FeedStageKind.SystemNotice, "待播的太多了，已跳过一条", true, TimeSpan.Zero),
        "队列已满，跳过提醒" => new(FeedStageKind.SystemNotice, "队列已满，跳过提醒", true, TimeSpan.Zero),
        _ => new(FeedStageKind.SystemNotice, stage.TrimEnd('。'), IsAlertMessage(stage), TimeSpan.Zero)
    };

    public static bool IsAlertMessage(string text) =>
        text.Contains("失败", StringComparison.Ordinal) ||
        text.Contains("错误", StringComparison.Ordinal) ||
        text.Contains("跳过", StringComparison.Ordinal) ||
        text.Contains("无效", StringComparison.Ordinal) ||
        text.Contains("超时", StringComparison.Ordinal) ||
        text.Contains("异常", StringComparison.Ordinal) ||
        text.Contains("拒绝", StringComparison.Ordinal);
}
