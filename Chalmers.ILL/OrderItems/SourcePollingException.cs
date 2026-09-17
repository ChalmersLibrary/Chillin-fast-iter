using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.OrderItems
{
    public class SourcePollingException : Exception
    {
        public SourcePollingException() { }
        public SourcePollingException(string message) : base(message) { }
        public SourcePollingException(string message, Exception inner) : base(message, inner) { }
    }
}