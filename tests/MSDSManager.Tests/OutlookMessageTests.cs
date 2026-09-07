using MSDSManager.Services;
using Xunit;

namespace MSDSManager.Tests;

public sealed class OutlookMessageTests
{
    [Theory]
    [InlineData(26, "calendar")]
    [InlineData(53, "meeting")]
    [InlineData(48, "task")]
    [InlineData(40, "contact")]
    public void Wrong_item_types_mention_inbox(int outlookClass, string kind)
    {
        var message = OutlookUserMessages.DescribeWrongItem(outlookClass);
        Assert.Contains(kind, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Inbox", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Paste_fallback_hint_is_always_present()
    {
        Assert.Contains("Match pasted text", OutlookUserMessages.PasteFallbackHint);
    }
}
