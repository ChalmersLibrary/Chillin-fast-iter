using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models.Mail;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Chalmers.ILL.Mail
{
    public class MicrosoftGraphMailWebApi : IMailWebApi
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MicrosoftGraphMailWebApi));

        // With client credentials flows the scopes is ALWAYS of the shape "resource/.default", as the
        // application permissions need to be set statically (in the portal or by PowerShell), and then granted by
        // a tenant administrator
        private string[] scopes = new string[] { "https://graph.microsoft.com/.default" };

        private IConfidentialClientApplication _app;
        private HttpClient _httpClient;
        private IChillinConfiguration _config;

        public MicrosoftGraphMailWebApi(HttpClient httpClient, IChillinConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        /// <summary>
        /// Connect to Exchange Service with credentials
        /// </summary>
        /// <returns>The Service reference</returns>
        public void ConnectToExchangeService(string username, string password)
        {
            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            _app = ConfidentialClientApplicationBuilder.Create(_config.MicrosoftGraphClientId)
                .WithClientSecret(_config.MicrosoftGraphClientSecret)
                .WithAuthority(new Uri(_config.MicrosoftGraphAuthority))
                .Build();
        }

        public string ArchiveMailMessage(MailQueueModel mqm)
        {
            // Find out Year and Month to archive on
            string year = DateTime.UtcNow.Year.ToString();
            string month = DateTime.UtcNow.Month.ToString().PadLeft(2, '0');
            var parts = mqm.DateTimeReceived.Split('-');
            if (parts.Length >= 2)
            {
                year = parts[0].Trim();
                month = parts[1].Trim();
            }
            else
            {
                // Probably american format
                parts = mqm.DateTimeReceived.Split(' ')[0].Split('/');
                if (parts.Length >= 3)
                {
                    year = parts[2].Trim();
                    month = parts[0].Trim().PadLeft(2, '0');
                }
            }

            // Check if Year folder exists below Inbox
            var isFirstYearRequest = true;
            dynamic rootMailFoldersResponse = null;
            dynamic yearFolder = null;
            while (yearFolder == null && (isFirstYearRequest || rootMailFoldersResponse["@odata.nextLink"] != null))
            {
                var url = isFirstYearRequest ?
                    _config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/childFolders" :
                    rootMailFoldersResponse["@odata.nextLink"].ToString();
                rootMailFoldersResponse = GetFromMicrosoftGraph(url);
                var rootMailFolders = rootMailFoldersResponse.value as IEnumerable<dynamic>;
                yearFolder = rootMailFolders.FirstOrDefault(x => x.displayName.ToString().Trim() == year);

                isFirstYearRequest = false;
            }

            // Create folder for Year if it wasn't found
            if (yearFolder == null)
            {
                yearFolder = PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/childFolders",
                    "{ \"displayName\":\"" + year + "\" }");
            }

            // Check if Month folder exists below Year folder
            var isFirstMonthRequest = true;
            dynamic childMailFoldersResponse = null;
            dynamic monthFolder = null;
            while (monthFolder == null && (isFirstMonthRequest || childMailFoldersResponse["@odata.nextLink"] != null))
            {
                var url = isFirstMonthRequest ?
                    _config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/" + yearFolder.id + "/childFolders" :
                    childMailFoldersResponse["@odata.nextLink"].ToString();
                childMailFoldersResponse = GetFromMicrosoftGraph(url);
                var childMailFolders = childMailFoldersResponse.value as IEnumerable<dynamic>;
                monthFolder = childMailFolders.FirstOrDefault(x => x.displayName.ToString().Trim() == month);

                isFirstMonthRequest = false;
            }

            // Create folder for Month if it wasn't found
            if (monthFolder == null)
            {
                monthFolder = PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/" + yearFolder.id + "/childFolders",
                    "{ \"displayName\":\"" + month + "\" }");
            }

            // Move the Mail Message to Month folder below Year folder
            PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/messages/" + mqm.Id + "/move", "{ \"destinationId\":\"" + monthFolder.id + "\" }");

            // Return the FolderId which we moved to
            return monthFolder.id;
        }

        public void ForwardMailMessage(MailQueueModel mqm, string recipientAddress, string extraText = "", bool delete = true)
        {
            // Create the prefixed content to add to the forwarded message body.
            string messageBodyPrefix = extraText == "" ? "Detta meddelande har vidarebefodrats av Chalmers.ILL för " + mqm.From + " <" + mqm.Sender + ">" : extraText;

            // Send the forwarded message
            var msg = "{ \"comment\": \"" + messageBodyPrefix + "\", \"toRecipients\": [{ \"emailAddress\": { \"address\": \"" + recipientAddress + "\" } } ] }";
            PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/messages/" + mqm.Id + "/forward", msg);

            if (delete)
            {
                DeleteToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/messages/" + mqm.Id);
            }
        }

        public List<MailQueueModel> ReadMailQueue()
        {
            var res = new List<MailQueueModel>();

            try
            {
                var mailInboxData = GetFromMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/messages");
                    
                foreach (var mailData in mailInboxData.value)
                {
                    var m = new MailQueueModel();

                    // Parses the hidden "chalmers.ill.orderitem" div (if present) into
                    // PatronName/PatronEmail/PatronCardNo/OriginalOrder/IsPurchaseRequest/
                    // DeliveryLibrary, and always sets MessageBody from the plain-text rendering.
                    // Shared with Isolated.FileMailWebApi (fas 6, isolerat läge steg A) so the fake
                    // exercises the same parsing rules instead of only the transport.
                    IncomingMailParser.ParseIncomingMailBody(m, mailData.body.content.ToString());

                    // Load all attachments
                    var attachmentList = new List<MailAttachment>();
                    if ((bool)(mailData.hasAttachments as JValue))
                    {
                        var attachments = GetFromMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/messages/" + 
                            mailData.id + "/attachments");
                        foreach (var attachmentData in attachments.value)
                        {
                            if (attachmentData != null && attachmentData["@odata.type"] != null && attachmentData["@odata.type"].ToString() == "#microsoft.graph.fileAttachment")
                            {
                                var memStream = new System.IO.MemoryStream(Convert.FromBase64String(attachmentData.contentBytes.ToString()));
                                attachmentList.Add(new MailAttachment(attachmentData.name.ToString(), memStream, attachmentData.contentType.ToString()));
                            }
                        }
                    }

                    // Bind different fields to MailQueueModel
                    m.Id = mailData.id.ToString();
                    m.To = mailData.toRecipients[0].emailAddress.address.ToString();
                    m.From = mailData.from.emailAddress.address.ToString();
                    m.Sender = mailData.from.emailAddress.name.ToString();
                    m.Debug = mailData.body.content.ToString();
                    m.Subject = mailData.subject.ToString();
                    m.DateTimeReceived = mailData.receivedDateTime.ToString().Replace("T", " ").Remove(16).Trim();
                    m.Attachments = attachmentList;

                    PatchToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/inbox/messages/" + m.Id, "{ \"isRead\":true }");

                    // Add this message to list
                    res.Add(m);
                }
            }
            catch (Exception e)
            {
                _log.Error("Failed fetching mail from Microsoft Graph.", e);
            }

            return res;
        }

        public void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit)
        {
            // Fetch mail ids from given folder where the messages are older than old limit
            var getUrl = _config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/" + folder + "/messages?$orderby=sentDateTime%20asc&$select=sentDateTime,id&$filter=sentDateTime%20lt%20" + oldLimit.ToString("yyyy-MM-dd") + "&$top=100";
            var mailInboxData = GetFromMicrosoftGraph(getUrl);

            // Iterate through the list of mail ids and delete each one of them
            foreach (var mailData in mailInboxData.value)
            {
                var deleteUrl = _config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/mailFolders/" + folder + "/messages/" + mailData.id;
                DeleteToMicrosoftGraph(deleteUrl);
            }
        }

        public void SendMailMessage(string orderId, string body, string subject, string recipientName, string recipientAddress, IDictionary<string, byte[]> attachments)
        {
            string senderEmail = _config.ChalmersIllSenderAddress;

            dynamic message = new ExpandoObject();
            message.subject = subject + " #" + orderId;

            dynamic messageBody = new ExpandoObject();
            messageBody.contentType = "html";
            messageBody.content = body.Replace("\n", "<br />");
            message.body = messageBody;

            message.toRecipients = new List<dynamic>();

            foreach (var singleRecipientAddress in recipientAddress.Split(',').Select(x => x.Trim()).Where(x => !String.IsNullOrEmpty(x)))
            {
                dynamic toRecipient = new ExpandoObject();
                dynamic toEmailAddress = new ExpandoObject();
                toEmailAddress.address = singleRecipientAddress;
                toRecipient.emailAddress = toEmailAddress;
                message.toRecipients.Add(toRecipient);
            }

            dynamic replyToRecipient = new ExpandoObject();
            dynamic replyToEmailAddress = new ExpandoObject();
            replyToEmailAddress.address = senderEmail;
            replyToRecipient.emailAddress = replyToEmailAddress;
            message.replyTo = new List<dynamic>() { replyToRecipient };

            message.attachments = new List<dynamic>();

            IDictionary<string, byte[]> largeAttachments = new Dictionary<string, byte[]>();
            foreach (var attachment in attachments)
            {
                if (attachment.Value.Length < 3000000)
                {
                    dynamic fileAttachment = new ExpandoObject();
                    fileAttachment.name = attachment.Key;
                    fileAttachment.contentBytes = Convert.ToBase64String(attachment.Value);
                    var dictFileAttachment = (IDictionary<string, object>)fileAttachment;
                    dictFileAttachment.Add("@odata.type", "#microsoft.graph.fileAttachment");
                    message.attachments.Add(dictFileAttachment);
                }
                else
                {
                    // Upload these after the email has been created on the mail server
                    largeAttachments.Add(attachment.Key, attachment.Value);
                }
            }

            dynamic messageContainer = new ExpandoObject();
            messageContainer.message = message;

            if (largeAttachments.Count > 0)
            {
                // Create the email on the mail server
                var uploadedMail = PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/messages", JsonConvert.SerializeObject(message));

                // Upload large files
                foreach (var largeAttachment in largeAttachments)
                {
                    UploadAndConnectToEmail(largeAttachment, Convert.ToString(uploadedMail.id));
                }

                // Send it all away
                PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/messages/" + uploadedMail.id + "/send");
            }
            else
            {
                // Send the email message and save a copy.
                PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/sendMail", JsonConvert.SerializeObject(messageContainer));
            }
        }

        public void SendPlainMailMessage(string body, string subject, string recipientAddress)
        {
            string senderEmail = _config.ChalmersIllSenderAddress;

            dynamic message = new ExpandoObject();
            message.subject = subject;

            dynamic messageBody = new ExpandoObject();
            messageBody.contentType = "html";
            messageBody.content = body.Replace("\n", "<br />");
            message.body = messageBody;

            dynamic toRecipient = new ExpandoObject();
            dynamic toEmailAddress = new ExpandoObject();
            toEmailAddress.address = recipientAddress;
            toRecipient.emailAddress = toEmailAddress;
            message.toRecipients = new List<dynamic>() { toRecipient };

            dynamic replyToRecipient = new ExpandoObject();
            dynamic replyToEmailAddress = new ExpandoObject();
            replyToEmailAddress.address = senderEmail;
            replyToRecipient.emailAddress = replyToEmailAddress;
            message.replyTo = new List<dynamic>() { replyToRecipient };

            dynamic messageContainer = new ExpandoObject();
            messageContainer.message = message;

            // Send the email message and save a copy.
            PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/sendMail", JsonConvert.SerializeObject(messageContainer));
        }

        private void UploadAndConnectToEmail(KeyValuePair<string, byte[]> attachment, string mailId)
        {
            dynamic createUploadSessionContainer = new ExpandoObject();
            dynamic createUploadSessionAttachmentItem = new ExpandoObject();
            createUploadSessionAttachmentItem.attachmentType = "file";
            createUploadSessionAttachmentItem.name = attachment.Key;
            createUploadSessionAttachmentItem.size = attachment.Value.Length;
            createUploadSessionContainer.AttachmentItem = createUploadSessionAttachmentItem;
            var uploadSessionResponse = PostToMicrosoftGraph(_config.MicrosoftGraphApiEndpoint + "/users/" + _config.MicrosoftGraphApiUserId + "/messages/" + mailId +
                "/attachments/createUploadSession", JsonConvert.SerializeObject(createUploadSessionContainer));
            var uploadUrl = Convert.ToString(uploadSessionResponse.uploadUrl);
            var currentBytePos = 0;

            while (uploadSessionResponse != null && uploadSessionResponse.nextExpectedRanges != null)
            {
                var endBytePos = currentBytePos + 3000000;
                endBytePos = endBytePos > attachment.Value.Length ? attachment.Value.Length : endBytePos;
                var chunkSize = endBytePos - currentBytePos;

                var chunk = new byte[chunkSize];
                Array.Copy(attachment.Value, currentBytePos, chunk, 0, chunkSize);

                string contentRangeHeaderValue = "bytes " + currentBytePos + "-" + (endBytePos - 1) + "/" + attachment.Value.Length;
                uploadSessionResponse = PutToUpload(uploadUrl, chunk, new Dictionary<string, string>()
                {
                    { "Content-Type", "application/octet-stream" },
                    { "Content-Length", chunkSize.ToString() },
                    { "Content-Range", contentRangeHeaderValue }
                });

                if (uploadSessionResponse != null && uploadSessionResponse.nextExpectedRanges != null && uploadSessionResponse.nextExpectedRanges.Count > 0)
                {
                    currentBytePos = int.Parse(Convert.ToString(uploadSessionResponse.nextExpectedRanges[0]));
                }
            }
        }

        private dynamic GetFromMicrosoftGraph(string url)
        {
            dynamic res = null;

            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var tokenTask = _app.AcquireTokenForClient(scopes).ExecuteAsync();
            tokenTask.Wait();
            var tokenResult = tokenTask.Result;

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, url))
            {
                requestMessage.Headers.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

                var sendTask = _httpClient.SendAsync(requestMessage);
                sendTask.Wait();
                var response = sendTask.Result;
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Non success status code [" + response.StatusCode + "] on get from Microsoft Graph: " + response.ReasonPhrase);
                }
                var readTask = response.Content.ReadAsStringAsync();
                readTask.Wait();
                res = JsonConvert.DeserializeObject<dynamic>(readTask.Result);
            }

            return res;
        }

        private dynamic PostToMicrosoftGraph(string url, string data = null)
        {
            dynamic res = null;

            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var tokenTask = _app.AcquireTokenForClient(scopes).ExecuteAsync();
            tokenTask.Wait();
            var tokenResult = tokenTask.Result;

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, url))
            {
                requestMessage.Headers.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
                if (data != null)
                {
                    requestMessage.Content = new StringContent(data,
                        Encoding.UTF8,
                        "application/json");
                }

                var sendTask = _httpClient.SendAsync(requestMessage);
                sendTask.Wait();
                var response = sendTask.Result;
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Non success status code [" + response.StatusCode + "] on post to Microsoft Graph: " + response.ReasonPhrase);
                }
                var readTask = response.Content.ReadAsStringAsync();
                readTask.Wait();
                res = JsonConvert.DeserializeObject<dynamic>(readTask.Result);
            }

            return res;
        }

        private dynamic PutToUpload(string url, byte[] data, IDictionary<string, string> headers = null)
        {
            dynamic res = null;

            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Put, url))
            {
                requestMessage.Headers.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Content = new ByteArrayContent(data);

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> newHeader in headers)
                    {
                        if (newHeader.Key.Contains("Content"))
                        {
                            requestMessage.Content.Headers.Add(newHeader.Key, newHeader.Value);
                        }
                        else
                        {
                            requestMessage.Headers.Add(newHeader.Key, newHeader.Value);
                        }
                    }
                }

                var sendTask = _httpClient.SendAsync(requestMessage);
                sendTask.Wait();
                var response = sendTask.Result;
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Non success status code [" + response.StatusCode + "] on put to Microsoft Graph: " + response.ReasonPhrase);
                }
                var readTask = response.Content.ReadAsStringAsync();
                readTask.Wait();
                res = JsonConvert.DeserializeObject<dynamic>(readTask.Result);
            }

            return res;
        }

        private dynamic DeleteToMicrosoftGraph(string url)
        {
            dynamic res = null;

            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var tokenTask = _app.AcquireTokenForClient(scopes).ExecuteAsync();
            tokenTask.Wait();
            var tokenResult = tokenTask.Result;

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Delete, url))
            {
                requestMessage.Headers.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

                var sendTask = _httpClient.SendAsync(requestMessage);
                sendTask.Wait();
                var response = sendTask.Result;
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Non success status code [" + response.StatusCode + "] on delete to Microsoft Graph: " + response.ReasonPhrase);
                }
                var readTask = response.Content.ReadAsStringAsync();
                readTask.Wait();
                res = JsonConvert.DeserializeObject<dynamic>(readTask.Result);
            }

            return res;
        }

        private dynamic PatchToMicrosoftGraph(string url, string data)
        {
            dynamic res = null;

            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var tokenTask = _app.AcquireTokenForClient(scopes).ExecuteAsync();
            tokenTask.Wait();
            var tokenResult = tokenTask.Result;

            using (var requestMessage = new HttpRequestMessage(new HttpMethod("PATCH"), url))
            {
                requestMessage.Headers.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);
                requestMessage.Content = new StringContent(data,
                    Encoding.UTF8,
                    "application/json");

                var sendTask = _httpClient.SendAsync(requestMessage);
                sendTask.Wait();
                var response = sendTask.Result;
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Non success status code [" + response.StatusCode + "] on patch to Microsoft Graph: " + response.ReasonPhrase);
                }
                var readTask = response.Content.ReadAsStringAsync();
                readTask.Wait();
                res = JsonConvert.DeserializeObject<dynamic>(readTask.Result);
            }

            return res;
        }
    }
}