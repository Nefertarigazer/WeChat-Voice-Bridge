using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class MessageFeedPolicyTests
{
    [Fact]
    public void ClassifiesStagesIntoChatTransitionalAndSystemNotices()
    {
        Assert.Equal(FeedStageKind.Ignored, MessageFeedPolicy.Classify("文件通知 → 接收").Kind);
        Assert.Equal(FeedStageKind.Ignored, MessageFeedPolicy.Classify("查询请求 → 接收").Kind);
        Assert.Equal(FeedStageKind.Received, MessageFeedPolicy.Classify("已接收").Kind);

        var synth = MessageFeedPolicy.Classify("收到首个有效音频帧");
        Assert.Equal(FeedStageKind.Transitional, synth.Kind);
        Assert.Equal("正在合成…", synth.Text);
        Assert.Equal(MessageFeedPolicy.SynthesizingHintLifetime, synth.AutoHideAfter);

        var play = MessageFeedPolicy.Classify("已向音频设备提交播放");
        Assert.Equal(FeedStageKind.Transitional, play.Kind);
        Assert.Equal("正在播放…", play.Text);

        var done = MessageFeedPolicy.Classify("播放完成");
        Assert.Equal(FeedStageKind.Transitional, done.Kind);
        Assert.Equal("已朗读", done.Text);
        Assert.Equal(MessageFeedPolicy.PlayedHintLifetime, done.AutoHideAfter);

        var skipped = MessageFeedPolicy.Classify("跳过超长消息");
        Assert.Equal(FeedStageKind.SystemNotice, skipped.Kind);
        Assert.True(skipped.IsAlert);

        var cleared = MessageFeedPolicy.Classify("已清空待播队列");
        Assert.Equal(FeedStageKind.SystemNotice, cleared.Kind);
        Assert.Equal("已清空待播", cleared.Text);
        Assert.False(cleared.IsAlert);
    }

    [Fact]
    public void TimestampDividersFollowThreeMinuteGap()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 19, 20, 0, TimeSpan.FromHours(8));
        Assert.True(MessageFeedPolicy.ShouldInsertTimestamp(null, t0));
        Assert.False(MessageFeedPolicy.ShouldInsertTimestamp(t0, t0.AddMinutes(1)));
        Assert.False(MessageFeedPolicy.ShouldInsertTimestamp(t0, t0.AddMinutes(2.9)));
        Assert.True(MessageFeedPolicy.ShouldInsertTimestamp(t0, t0.AddMinutes(3)));
        Assert.Equal("19:20", MessageFeedPolicy.FormatTimestamp(t0));
    }
}
