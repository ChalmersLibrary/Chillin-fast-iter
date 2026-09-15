using Chalmers.ILL.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chalmers.ILL.Statistics
{
    public class DefaultStatCalc : IStatisticsCalculator
    {
        public int CalculateDataPointValue(IEnumerable<OrderItemModel> dataBag, string calculationTypeStr)
        {
            int res = 0;

            if (calculationTypeStr == "COUNT")
            {
                res = dataBag.Count();
            }
            else if (calculationTypeStr == "AVERAGE_ORDER_LENGTH")
            {
                res = (int)Math.Ceiling(dataBag.Select(CalculateTotalTurnaroundTime).DefaultIfEmpty().Average());
            }
            else if (calculationTypeStr == "MEDIAN_ORDER_LENGTH")
            {
                var timeDifferences = dataBag.Select(CalculateTotalTurnaroundTime).OrderBy(x => x);
                var count = timeDifferences.Count();
                if (count > 1)
                {
                    res = (int)Math.Ceiling((double)(timeDifferences.ElementAt(count / 2) + timeDifferences.ElementAt((count - 1) / 2)) / 2);
                }
                else if (count == 1)
                {
                    res = (int)timeDifferences.First();
                }
                else
                {
                    res = 0;
                }
            }
            else
            {
                throw new Exception("Unknown calculation type.");
            }

            return res;
        }

        #region private

        // Timezone-dependent latent bug found while running the test suite on this Linux
        // devcontainer instead of the original Windows dev machine: item.Log's timestamps carry
        // an explicit UTC offset (e.g. "+01:00" - an artifact of whatever machine originally
        // wrote DateTime.Now into the log, not a deliberate design choice), while
        // item.CreateDate (a plain "datetime" DB column, DateTimeKind.Unspecified) carries no
        // offset at all. Json.NET's default date parsing converts offset-qualified strings to
        // the *reading machine's* local time before subtracting - correct by coincidence on a
        // host whose local zone happened to match the offset in the data, silently wrong
        // elsewhere by exactly that difference. LiteralDateTimeConverter keeps the wall-clock
        // digits as written instead, matching how item.CreateDate is already being treated, so
        // the result no longer depends on the host's configured timezone.
        private static readonly JsonSerializerSettings _literalDateTimeSettings = new JsonSerializerSettings
        {
            Converters = { new LiteralDateTimeConverter() },
            DateParseHandling = DateParseHandling.None
        };

        private class LiteralDateTimeConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(DateTime);
            public override bool CanWrite => false;

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var value = reader.Value as string;
                return string.IsNullOrEmpty(value)
                    ? default(DateTime)
                    : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None).DateTime;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
                throw new NotSupportedException();
        }

        private int CalculateTotalTurnaroundTime(OrderItemModel item)
        {
            var log = JsonConvert.DeserializeObject<List<LogItem>>(item.Log, _literalDateTimeSettings);

            var startTime = item.CreateDate;
            var endTime = startTime;

            log.OrderByDescending(x => x.CreateDate);
            foreach (var logItem in log)
            {
                // Find the latest status change and declare that to be the latest
                if (logItem.Type == "STATUS")
                {
                    endTime = logItem.CreateDate;
                }
            }

            var test = (endTime - startTime).TotalMinutes;
            var test2 = (int)test;
            return (int)(endTime - startTime).TotalMinutes;
        }

        #endregion
    }
}