using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class StatisticsVariable
    {
        public string Name { get; set; }
        public List<string> LuceneQueries { get; set; }
        public string CalculationType { get; set; }
        public List<int> Values { get; set; }
    }

    public class StatisticsResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<StatisticsVariable> StatisticsData { get; set; }
    }

    public class StatisticsRequest
    {
        public List<StatisticsVariable> StatisticsData { get; set; }
    }
}