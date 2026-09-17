using Chalmers.ILL.Configuration;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chalmers.ILL.Mail
{
    public class MailService : IMailService
    {
        IMediaItemManager _mediaItemManager;
        IMailWebApi _exchangeMailWebApi;
        IChillinConfiguration _config;

        public MailService(IMediaItemManager mediaItemManager, IMailWebApi exchangeMailWebApi, IChillinConfiguration config)
        {
            _mediaItemManager = mediaItemManager;
            _exchangeMailWebApi = exchangeMailWebApi;
            _config = config;
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
            _exchangeMailWebApi.ConnectToExchangeService(_config.ChalmersIllExchangeLogin, _config.ChalmersIllExchangePassword);
            _exchangeMailWebApi.SendMailMessage(mailModel.OrderId, body, _config.ChalmersIllMailSubject, mailModel.recipientName, mailModel.recipientEmail, attachments);
        }

        public void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit)
        {
            _exchangeMailWebApi.ConnectToExchangeService(_config.ChalmersIllExchangeLogin, _config.ChalmersIllExchangePassword);
            _exchangeMailWebApi.DeleteOldMessagesFromFolder(folder, oldLimit);
        }
    }
}