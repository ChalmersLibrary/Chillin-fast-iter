using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Exceptions
{
    public class ItemNotFoundException : Exception
    {
        public ItemNotFoundException() : base() { }
        public ItemNotFoundException(string msg) : base(msg) { }
        public ItemNotFoundException(string msg, Exception inner) : base(msg, inner) { }
    }
}