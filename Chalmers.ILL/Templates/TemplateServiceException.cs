using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Templates
{
    public class TemplateServiceException : Exception
    {
        public TemplateServiceException() { }
        public TemplateServiceException(string message) : base(message) { }
        public TemplateServiceException(string message, Exception inner) : base(message, inner) { }
    }
}