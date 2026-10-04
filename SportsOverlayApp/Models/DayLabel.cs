using System;
using System.Globalization;

namespace SportsOverlayApp.Models
{
    /// <summary>
    /// The day shown before a kick-off time for games not played today, so a
    /// game days away doesn't read as today's: "Tue 08:00" within the coming
    /// week, "12 Oct 08:00" further out (or for a past date). English, like
    /// the rest of the UI, whatever the system locale.
    /// </summary>
    public static class DayLabel
    {
        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

        public static string Prefix(DateTime date)
        {
            var days = (date.Date - DateTime.Today).Days;
            return days >= 1 && days <= 6 ? date.ToString("ddd", En) : date.ToString("d MMM", En);
        }

        /// <summary>
        /// Parses FlashScore's "dd.MM" day (no year) as the nearest such date
        /// to today, so a December game seen in January lands in the right year.
        /// </summary>
        public static bool TryParse(string ddMM, out DateTime date)
        {
            date = default;
            var parts = (ddMM ?? "").Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var d) || !int.TryParse(parts[1], out var m)
                || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(DateTime.Today.Year, m))
                return false;
            var today = DateTime.Today;
            date = new DateTime(today.Year, m, d);
            if ((date - today).TotalDays > 183) date = date.AddYears(-1);
            else if ((today - date).TotalDays > 183) date = date.AddYears(1);
            return true;
        }
    }
}
