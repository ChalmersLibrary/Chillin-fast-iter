using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Models.Mail
{
    public class MailAttachment
    {
        public MailAttachment()
        {
            // NOP
        }

        public MailAttachment(string newTitle, System.IO.MemoryStream newData, string contentType)
        {
            Title = newTitle;
            Data = newData;
            ContentType = contentType;
        }

        public string Title { get; set; }
        public System.IO.MemoryStream Data { get; set; }
        public string ContentType { get; set; }
    }
}