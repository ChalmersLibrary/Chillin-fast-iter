using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.MediaItems
{
    public class MediaItemNotFoundException : Exception
    {
        public MediaItemNotFoundException(string msg) : base(msg) { }
    }
}