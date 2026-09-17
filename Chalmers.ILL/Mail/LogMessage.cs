using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Mail
{
    public class LogMessage
    {
        public string type { get; set; }
        public string message { get; set; }

        public LogMessage(string type, string message)
        {
            this.type = type;
            this.message = message;
        }
    }
}