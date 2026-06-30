#nullable enable
using System;
using System.Globalization;
using System.Windows.Data;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Buckets a <see cref="HistoryEntryDto.ExecutedAt"/> ISO-8601 timestamp into one of four
    /// coarse date groups used by the SQL History master list grouping:
    /// <c>Today</c> / <c>This Week</c> / <c>Two Months Ago</c> / <c>Older</c>.
    /// <para>
    /// Used as the converter on a <see cref="System.Windows.Data.PropertyGroupDescription"/> over
    /// <see cref="HistoryEntryDto.ExecutedAt"/>. WPF group order follows item order, so the buckets
    /// render newest-first only when the source collection is sorted descending (the engine returns
    /// history newest-first).
    /// </para>
    /// <para>
    /// The four buckets are contiguous and cover every date: <c>Today</c> (the same calendar day),
    /// <c>This Week</c> (the preceding six days), <c>Two Months Ago</c> (7 to 59 days back), and
    /// <c>Older</c> (60+ days). The labels mirror the Red Gate SQL Prompt history reference; there
    /// is no separate "This Month" bucket by design.
    /// </para>
    /// </summary>
    internal sealed class DateBucketConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Delegate the bucket classification to the shared, edition-independent Core helper so the
            // web and desktop History surfaces stay in lockstep. now=DateTime.Now → now.Date == today.
            return AkmlSql.Core.Models.History.HistoryDateBucket.Of(value as string, DateTime.Now);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
