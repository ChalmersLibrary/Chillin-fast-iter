using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Mail
{
    public class MailOperationResult
    {
        public bool Success { get; set; }
        public DelayedMailOperation MailOperation { get; set; }
        public string Message { get; set; }
    }
}