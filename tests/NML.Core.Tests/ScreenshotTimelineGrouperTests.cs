using NML.Core.Logging;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ScreenshotTimelineGrouper"/> — the pure logic behind the timeline screenshot
/// browse: groups items by capture date, newest-group first, and formats headers as Today/Yesterday/ISO.
/// </summary>
public class ScreenshotTimelineGrouperTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Group_Newest_Date_First()
    {
        var items = new[]
        {
            ("old.png",  Now.AddDays(-5), "/mc/old.png"),
            ("today.png", Now.AddHours(-2), "/mc/today.png"),
            ("yesterday.png", Now.AddDays(-1), "/mc/y.png"),
        };
        var groups = ScreenshotTimelineGrouper.Group(items, Now);
        groups.Should().HaveCount(3);
        groups[0].Header.Should().Be("Today");
        groups[1].Header.Should().Be("Yesterday");
        groups[2].Header.Should().Be((Now.AddDays(-5)).UtcDateTime.Date.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Group_Items_Within_Same_Day_Are_In_One_Group_Newest_First()
    {
        var day = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var items = new (string, DateTimeOffset, string)[]
        {
            ("morning.png", day.AddHours(8), "/mc/morning.png"),
            ("afternoon.png", day.AddHours(15), "/mc/afternoon.png"),
            ("noon.png", day.AddHours(12), "/mc/noon.png"),
        };
        var groups = ScreenshotTimelineGrouper.Group(items, Now);
        groups.Should().ContainSingle();
        var g = groups[0];
        g.Header.Should().Be("Today");
        g.Items.Should().HaveCount(3);
        // Newest-first within the group.
        g.Items[0].Name.Should().Be("afternoon.png");
        g.Items[1].Name.Should().Be("noon.png");
        g.Items[2].Name.Should().Be("morning.png");
    }

    [Fact]
    public void Group_Empty_Input_Returns_Empty()
    {
        ScreenshotTimelineGrouper.Group(Array.Empty<(string, DateTimeOffset, string)>(), Now).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Yesterday")]
    [InlineData(2, "2024-06-13")]
    [InlineData(30, "2024-05-16")]
    public void FormatHeader_Relative_Labels(int daysAgo, string expected)
    {
        var date = Now.UtcDateTime.Date.AddDays(-daysAgo);
        ScreenshotTimelineGrouper.FormatHeader(date, Now.UtcDateTime.Date).Should().Be(expected);
    }

    [Fact]
    public void Group_Across_Months_Boundary()
    {
        // Screenshots from June 1 and May 31 should be in separate groups with ISO-date headers.
        var items = new (string, DateTimeOffset, string)[]
        {
            ("jun1.png", new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero), "/mc/jun1.png"),
            ("may31.png", new DateTimeOffset(2024, 5, 31, 22, 0, 0, TimeSpan.Zero), "/mc/may31.png"),
        };
        var groups = ScreenshotTimelineGrouper.Group(items, Now);
        groups.Should().HaveCount(2);
        groups[0].Header.Should().Be("2024-06-01"); // newer group first
        groups[1].Header.Should().Be("2024-05-31");
    }
}
