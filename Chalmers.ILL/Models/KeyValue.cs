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
        public List<string> AvailableValues { get; set; }
    }
}