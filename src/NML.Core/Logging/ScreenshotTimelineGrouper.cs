using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NML.Core.Logging;

/// <summary>
/// Groups screenshots into date-keyed timeline sections (Today / Yesterday / yyyy-MM-dd), newest
/// first, so the screenshot grid renders a chronological browse instead of a flat list. Pure +
/// unit-tested; the VM feeds it the per-card (name, timestamp) pairs and binds the resulting groups.
/// </summary>
public static class ScreenshotTimelineGrouper
{
    /// <summary>Group <paramref name="items"/> by their capture date, newest-group first. Within
    /// each group, items are ordered newest-first. The section header is "Today"/"Yesterday"/ISO date
    /// relative to <paramref name="now"/> (UTC).</summary>
    public static IReadOnlyList<ScreenshotTimelineGroup> Group(
        IEnumerable<(string Name, DateTimeOffset Timestamp)> items,
        DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var referenceDate = reference.UtcDateTime.Date;

        var groups = items
            .GroupBy(item => item.Timestamp.UtcDateTime.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new ScreenshotTimelineGroup(
                Header: FormatHeader(g.Key, referenceDate),
                Items: g.OrderByDescending(item => item.Timestamp)
                        .Select(item => new ScreenshotTimelineItem(item.Name, item.Timestamp))
                        .ToList()))
            .ToList();
        return groups;
    }

    /// <summary>Format a date as a human-readable section header: "Today" / "Yesterday" / ISO date.</summary>
    public static string FormatHeader(DateTime date, DateTime referenceDate)
    {
        int diffDays = (referenceDate - date).Days;
        return diffDays switch
        {
            0 => "Today",
            1 => "Yesterday",
            _ => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>A date section in the timeline: a header label + the screenshots captured on that date.</summary>
public sealed record ScreenshotTimelineGroup(string Header, IReadOnlyList<ScreenshotTimelineItem> Items);

/// <summary>One screenshot in a timeline group.</summary>
public sealed record ScreenshotTimelineItem(string Name, DateTimeOffset Timestamp);
