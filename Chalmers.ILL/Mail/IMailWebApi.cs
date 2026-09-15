using Chalmers.ILL.Models.Mail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chalmers.ILL.Mail
{
    public interface IMailWebApi
    {
        void ConnectToExchangeService(string username, string password);
        List<MailQueueModel> ReadMailQueue();
        void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit);
        string ArchiveMailMessage(MailQueueModel mqm);
        void ForwardMailMessage(MailQueueModel mqm, string recipientAddress, string extraText = "", bool delete = true);
        void SendMailMessage(string orderId, string body, string subject, string recipientName, string recipientAddress, IDictionary<string, byte[]> attachments);
        void SendPlainMailMessage(string body, string subject, string recipientAddress);
    }
}
