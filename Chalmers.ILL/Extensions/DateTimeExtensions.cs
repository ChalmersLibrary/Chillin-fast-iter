using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chalmers.ILL.Extensions
{
    public static class DateTimeExtensions
    {
        public static string ToPrettyString(this DateTime date)
        {
            var ret = date.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

            if ((DateTime.Now - date).TotalMinutes < 60) // Inom en timme
            {
                var m = Math.Floor((DateTime.Now - date).TotalMinutes);
                ret = m + " " + (m == 1 ? "minut" : "minuter");
            }
            else if (DateTime.Now.Date == date.Date) // Samma dag
            {
                var h = Math.Floor((DateTime.Now - date).TotalHours);
                ret = h + " " + (h == 1 ? "timme" : "timmar");
            }
            else if (DateTime.Now.AddDays(-1).Date == date.Date) // I går
            {
                ret = "i går";
            }
            else if ((DateTime.Now.Date - date.Date).TotalDays < 30) // Inom de senaste trettio dagarna
            {
                ret = Math.Floor((DateTime.Now.Date - date.Date).TotalDays) + " dagar";
            }
            else if ((DateTime.Now.Date - date.Date).TotalDays < 365) // Inom det senaste året
            {
                ret = Math.Floor((DateTime.Now.Date - date.Date).TotalDays / 7) + " veckor";
            }
            else // Gammal
            {
                ret = Math.Floor((DateTime.Now.Date - date.Date).TotalDays / 365) + " år";
            }

            return ret;
        }
    }
}