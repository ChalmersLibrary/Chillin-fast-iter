using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Mail;
using Chalmers.ILL.Models.Mail;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // File-based IMailWebApi for isolated mode (fas 6, isolerat läge steg A) - the most important
    // fake of the bunch, since Graph sends real mail to real patrons and there was previously no
    // test double for it at all. Everything lives under DataPath/mail:
    //  - inbox/{id}.html   - drop a file here to simulate an incoming message; ReadMailQueue()
    //                        parses it with IncomingMailParser, same rules as the real Graph
    //                        implementation, not just the transport.
    //  - outbox/{stamp}/   - every outgoing send writes message.json + body.html (openable directly
    //                        in a browser) + attachment files here, one subfolder per message.
    //  - archive/{yyyy}/{mm}/ - where ArchiveMailMessage moves a processed inbox message to.
    public class FileMailWebApi : IMailWebApi
    {
        private readonly IChillinConfiguration _config;

        public FileMailWebApi(IChillinConfiguration config)
        {
            _config = config;
        }

        private string MailRoot => Path.Combine(_config.DataPath, "mail");
        private string InboxDirectory => Path.Combine(MailRoot, "inbox");
        private string OutboxDirectory => Path.Combine(MailRoot, "sentitems");
        private string ArchiveDirectory => Path.Combine(MailRoot, "archive");

        public void ConnectToExchangeService(string username, string password)
        {
            // No-op - isolated mode never talks to a real mail server.
        }

        public List<MailQueueModel> ReadMailQueue()
        {
            var res = new List<MailQueueModel>();

            if (!Directory.Exists(InboxDirectory))
                return res;

            foreach (var htmlFile in Directory.GetFiles(InboxDirectory, "*.html").OrderBy(f => f))
            {
                var id = Path.GetFileNameWithoutExtension(htmlFile);
                var metaFile = Path.Combine(InboxDirectory, id + ".meta.json");
                var meta = File.Exists(metaFile)
                    ? JsonConvert.DeserializeObject<IncomingMailMeta>(File.ReadAllText(metaFile))
                    : new IncomingMailMeta();

                var bodyContent = File.ReadAllText(htmlFile);

                var m = new MailQueueModel
                {
                    Id = id,
                    To = meta.To ?? _config.MicrosoftGraphApiUserId,
                    From = meta.From ?? "patron@isolated.invalid",
                    Sender = meta.Sender ?? "Isolated Test Patron",
                    Subject = meta.Subject ?? id,
                    Debug = bodyContent,
                    DateTimeReceived = meta.DateTimeReceived ?? File.GetLastWriteTime(htmlFile).ToString("yyyy-MM-dd HH:mm"),
                    Attachments = new List<MailAttachment>()
                };

                IncomingMailParser.ParseIncomingMailBody(m, bodyContent);

                res.Add(m);
            }

            return res;
        }

        public void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit)
        {
            var directory = ResolveFolder(folder);
            if (!Directory.Exists(directory))
                return;

            foreach (var entry in Directory.GetFileSystemEntries(directory))
            {
                var lastWrite = File.GetLastWriteTime(entry);
                if (lastWrite < oldLimit)
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else
                        File.Delete(entry);
                }
            }
        }

        public string ArchiveMailMessage(MailQueueModel mqm)
        {
            var now = DateTime.UtcNow;
            var targetDirectory = Path.Combine(ArchiveDirectory, now.Year.ToString(), now.Month.ToString("00"));
            Directory.CreateDirectory(targetDirectory);

            MoveInboxMessage(mqm.Id, targetDirectory);

            return Path.GetRelativePath(MailRoot, targetDirectory);
        }

        public void ForwardMailMessage(MailQueueModel mqm, string recipientAddress, string extraText = "", bool delete = true)
        {
            var comment = extraText == ""
                ? "Detta meddelande har vidarebefodrats av Chalmers.ILL för " + mqm.From + " <" + mqm.Sender + ">"
                : extraText;

            WriteOutgoingMessage(
                to: recipientAddress,
                subject: "Fwd: " + mqm.Subject,
                bodyHtml: comment + "<hr/>" + mqm.Debug,
                orderId: mqm.OrderId,
                attachments: null);

            if (delete)
            {
                MoveInboxMessage(mqm.Id, null);
            }
        }

        public void SendMailMessage(string orderId, string body, string subject, string recipientName, string recipientAddress, IDictionary<string, byte[]> attachments)
        {
            WriteOutgoingMessage(recipientAddress, subject + " #" + orderId, body, orderId, attachments);
        }

        public void SendPlainMailMessage(string body, string subject, string recipientAddress)
        {
            WriteOutgoingMessage(recipientAddress, subject, body, orderId: null, attachments: null);
        }

        private string ResolveFolder(string folder) => folder == "sentitems"
            ? OutboxDirectory
            : Path.Combine(MailRoot, folder);

        // A message the real Graph implementation is still working through when ArchiveMailMessage
        // or a deleting ForwardMailMessage runs - moves it out of (or removes it from) the inbox so
        // ReadMailQueue() doesn't keep returning it forever, same as marking it read/moving it would
        // on a real mailbox.
        private void MoveInboxMessage(string id, string targetDirectory)
        {
            var htmlFile = Path.Combine(InboxDirectory, id + ".html");
            var metaFile = Path.Combine(InboxDirectory, id + ".meta.json");

            if (targetDirectory != null)
            {
                if (File.Exists(htmlFile))
                    File.Move(htmlFile, Path.Combine(targetDirectory, id + ".html"), overwrite: true);
                if (File.Exists(metaFile))
                    File.Move(metaFile, Path.Combine(targetDirectory, id + ".meta.json"), overwrite: true);
            }
            else
            {
                if (File.Exists(htmlFile))
                    File.Delete(htmlFile);
                if (File.Exists(metaFile))
                    File.Delete(metaFile);
            }
        }

        private void WriteOutgoingMessage(string to, string subject, string bodyHtml, string orderId, IDictionary<string, byte[]> attachments)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var messageDirectory = Path.Combine(OutboxDirectory, stamp);
            Directory.CreateDirectory(messageDirectory);

            File.WriteAllText(Path.Combine(messageDirectory, "body.html"), bodyHtml);

            var attachmentNames = new List<string>();
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    File.WriteAllBytes(Path.Combine(messageDirectory, attachment.Key), attachment.Value);
                    attachmentNames.Add(attachment.Key);
                }
            }

            var message = new OutgoingMailMeta
            {
                To = to,
                Subject = subject,
                OrderId = orderId,
                SentAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Attachments = attachmentNames
            };
            File.WriteAllText(Path.Combine(messageDirectory, "message.json"), JsonConvert.SerializeObject(message, Formatting.Indented));
        }

        private class IncomingMailMeta
        {
            public string To { get; set; }
            public string From { get; set; }
            public string Sender { get; set; }
            public string Subject { get; set; }
            public string DateTimeReceived { get; set; }
        }

        private class OutgoingMailMeta
        {
            public string To { get; set; }
            public string Subject { get; set; }
            public string OrderId { get; set; }
            public string SentAt { get; set; }
            public List<string> Attachments { get; set; }
        }
    }
}
