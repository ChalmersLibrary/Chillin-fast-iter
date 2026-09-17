using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.OrderItems
{
    public class OrderItemSearchIndexingException : Exception
    {
        public OrderItemSearchIndexingException() { }
        public OrderItemSearchIndexingException(string message) : base(message) { }
        public OrderItemSearchIndexingException(string message, Exception innerException) : base(message, innerException) { }
    }
}