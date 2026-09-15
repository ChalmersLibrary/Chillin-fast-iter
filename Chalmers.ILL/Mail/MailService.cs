using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Chalmers.ILL.Mail
{
    public class MailService : IMailService
    {
        IMediaItemManager _mediaItemManager;
        IMailWebApi _exchangeMailWebApi;

        public MailService(IMediaItemManager mediaItemManager, IMailWebApi exchangeMailWebApi)
        {
            _mediaItemManager = mediaItemManager;
            _exchangeMailWebApi = exchangeMailWebApi;
        }

        public void SendMail(OutgoingMailModel mailModel)
        {
            // Send mail to recipient
            var attachments = new Dictionary<string, byte[]>();
            if (mailModel.attachments != null)
            {
                foreach (var mediaId in mailModel.attachments)
                {
                    var mediaItem = _mediaItemManager.GetOne(mediaId);
                    if (mediaItem != null)
                    {
                        byte[] data = new byte[mediaItem.Data.Length];
                        mediaItem.Data.Read(data, 0, data.Length);
                        attachments.Add(mediaItem.Name, data);
                    }
                    else
                    {
                        throw new Exception("Failed to fetch media item for id " + mediaId + ".");
                    }

                    // Dispose stream that is no longer needed. Handle this in some better way?
                    mediaItem.Data.Dispose();
                }
            }
            string body = mailModel.message;
            _exchangeMailWebApi.ConnectToExchangeService(ConfigurationManager.AppSettings["chalmersIllExhangeLogin"], ConfigurationManager.AppSettings["chalmersIllExhangePass"]);
            _exchangeMailWebApi.SendMailMessage(mailModel.OrderId, body, ConfigurationManager.AppSettings["chalmersILLMailSubject"], mailModel.recipientName, mailModel.recipientEmail, attachments);
        }

        public void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit)
        {
            _exchangeMailWebApi.ConnectToExchangeService(ConfigurationManager.AppSettings["chalmersIllExhangeLogin"], ConfigurationManager.AppSettings["chalmersIllExhangePass"]);
            _exchangeMailWebApi.DeleteOldMessagesFromFolder(folder, oldLimit);
        }
    }
}