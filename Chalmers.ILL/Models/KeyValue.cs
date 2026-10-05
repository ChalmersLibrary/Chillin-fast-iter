using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models
{
    public class KeyValueResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<KeyValues> KeyValues { get; set; }
    }

    public class KeyValueRequest
    {
        public List<string> Keys { get; set; }
    }

    public class KeyValues
    {
        public string Name { get; set; }
        public string Key { get; set; }

        /// <summary>
        /// The name this field has in the search index, which is not the same as <see cref="Key"/>:
        /// Key is the C# property name (GetFieldValue reflects on it), while the index holds the
        /// camelCased JSON names - and two of the keys live under SierraInfo. The statistics page
        /// builds its filter queries from this, not from Key.
        /// </summary>
        public string QueryField { get; set; }

        public List<string> AvailableValues { get; set; }
    }
}