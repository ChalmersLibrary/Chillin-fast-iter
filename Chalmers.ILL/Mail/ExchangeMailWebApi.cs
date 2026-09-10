using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Exchange.WebServices.Data;
using System.Security;
using System.Runtime.InteropServices;
using System.Globalization;
using HtmlAgilityPack;
using System.Configuration;
using System.Diagnostics;
using Chalmers.ILL.Utilities;
using Chalmers.ILL.Models.Mail;

namespace Chalmers.ILL.Mail
{
    public class ExchangeMailWebApi : IExchangeMailWebApi
    {
        ExchangeService _service;

        /// <summary>
        /// Connect to Exchange Service with credentials
        /// </summary>
        /// <returns>The Service reference</returns>
        public void ConnectToExchangeService(string username, string password)
        {

            ServicePointManager.ServerCertificateValidationCallback = CertificateValidationCallBack;
            System.Uri exchangeUrl = new System.Uri(ConfigurationManager.AppSettings["chalmersIllExhangeWebServiceUrl"]);
            _service = new ExchangeService(ExchangeVersion.Exchange2010_SP1);

            // The Credentials for the Service Account to connect to
            _service.Credentials = new WebCredentials(username, password);

            // Debug flags
            //_service.TraceEnabled = true;
            //_service.TraceFlags = TraceFlags.All;

            // Connect to the EWS surface or die trying
            try
            {
                _service.Url = exchangeUrl;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public List<MailQueueModel> ReadMailQueue()
        {
            // Custom mail headers like this, whoah...
            // BUG/TODO: Doesn't work with custom headers like our X-CTHB, works with some headers...
            ExtendedPropertyDefinition MsgRefProperty = new ExtendedPropertyDefinition(DefaultExtendedPropertySet.InternetHeaders, "X-Msg-Ref", MapiPropertyType.String);
            ExtendedPropertyDefinition PatronNameProperty = new ExtendedPropertyDefinition(DefaultExtendedPropertySet.InternetHeaders, "X-CTHB-PatronName", MapiPropertyType.String);
            ExtendedPropertyDefinition PatronEmailProperty = new ExtendedPropertyDefinition(DefaultExtendedPropertySet.InternetHeaders, "X-CTHB-PatronEmail", MapiPropertyType.String);
            ExtendedPropertyDefinition PatronCardNoProperty = new ExtendedPropertyDefinition(DefaultExtendedPropertySet.InternetHeaders, "X-CTHB-PatronCardNo", MapiPropertyType.String);
            ExtendedPropertyDefinition OriginalOrderProperty = new ExtendedPropertyDefinition(DefaultExtendedPropertySet.InternetHeaders, "X-CTHB-OriginalOrder", MapiPropertyType.String);

            // Create a new List of MailQueueModel to return
            var list = new List<MailQueueModel>();

            // Construct a SearchFilter to find relevant items (not used right now)
            // http://blogs.msdn.com/b/akashb/archive/2010/03/05/how-to-build-a-complex-search-using-searchfilter-and-searchfiltercollection-in-ews-managed-api-1-0.aspx
            // http://stackoverflow.com/questions/18126804/exchange-web-services-finding-emails-sent-to-a-recipient

            List<SearchFilter> searchFilterCollection = new List<SearchFilter>();
            searchFilterCollection.Add(new SearchFilter.Exists(PatronNameProperty));
            SearchFilter searchFilter = new SearchFilter.SearchFilterCollection(LogicalOperator.And, searchFilterCollection.ToArray());

            // Create a view with a page size of 50.
            ItemView view = new ItemView(50);
            
            // Indicate that the base property and the User Property will be returned
            view.PropertySet = new PropertySet(BasePropertySet.FirstClassProperties, MsgRefProperty);
            
            // Order the search results by the DateTimeReceived in descending order.
            view.OrderBy.Add(ItemSchema.DateTimeReceived, SortDirection.Descending);
            
            // Set the traversal to shallow. (Shallow is the default option; other options are Associated and SoftDeleted.)
            view.Traversal = ItemTraversal.Shallow;

            // Find each mail in special Folder and matching SearchFilter, return max 50
            foreach (var mail in _service.FindItems(WellKnownFolderName.Inbox, view))
            {
                if (mail is EmailMessage)
                {
                    // Instanciate new MailQueueModel for this mail
                    var m = new MailQueueModel();

                    // Bind this mail.Id to full EmailMessage and specify additional Properties to read
                    EmailMessage message = EmailMessage.Bind(_service, mail.Id, new PropertySet(BasePropertySet.FirstClassProperties, MsgRefProperty));

                    // Load Message Body as HTML with HtmlAgilityPack
                    HtmlAgilityPack.HtmlDocument htmlDoc = new HtmlAgilityPack.HtmlDocument();
                    htmlDoc.LoadHtml(MailBodyFixer.RemoveHtmlAroundLinks(message.Body.Text));

                    // Query for OrderItem properties in MailQueueModel
                    if (htmlDoc.DocumentNode.HasChildNodes && htmlDoc.GetElementbyId("chalmers.ill.orderitem") != null)
                    {
                        m.PatronName = htmlDoc.GetElementbyId("PatronName").InnerText;
                        m.PatronEmail = htmlDoc.GetElementbyId("PatronEmail").InnerText;
                        m.PatronCardNo = htmlDoc.GetElementbyId("PatronCardNo").InnerText;
                        m.OriginalOrder = htmlDoc.GetElementbyId("OriginalOrder").InnerText;
                        if (htmlDoc.GetElementbyId("Purchase") != null)
                        {
                            m.IsPurchaseRequest = Convert.ToBoolean(htmlDoc.GetElementbyId("Purchase").InnerText);
                        }
                        else
                        {
                            m.IsPurchaseRequest = false;
                        }
                        m.DeliveryLibrary = htmlDoc.GetElementbyId("DeliveryLibrary").InnerText;
                    }

                    // Load all attachments
                    var attachmentList = new List<MailAttachment>();
                    foreach (var attachment in message.Attachments)
                    {
                        if (attachment is FileAttachment)
                        {
                            var fa = attachment as FileAttachment;
                            var memStream = new System.IO.MemoryStream();
                            fa.Load(memStream);
                            attachmentList.Add(new MailAttachment(fa.Name, memStream, fa.ContentType));
                        }
                    }

                    // Bind different fields to MailQueueModel
                    m.Id = mail.Id;
                    m.To = message.ToRecipients[0].Address;
                    m.From = message.From.Address;
                    m.Sender = message.From.Name;
                    m.Debug = message.Body;
                    m.Subject = mail.Subject;
                    m.DateTimeReceived = mail.DateTimeReceived.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    m.Attachments = attachmentList;

                    // Message body as text only
                    var sb = new StringBuilder();

                    if (htmlDoc != null && htmlDoc.DocumentNode != null)
                    {
                        var textNodes = htmlDoc.DocumentNode.SelectNodes("//text()");
                        if (textNodes != null)
                        {
                            foreach (HtmlNode node in textNodes)
                            {
                                if (!node.HasChildNodes)
                                {
                                    string text = node.InnerText;
                                    if (!string.IsNullOrEmpty(text))
                                        sb.AppendLine(text.Trim());
                                }
                            }
                        }
                    }

                    var messageText = sb.ToString();
                    m.MessageBody = Helpers.HtmlToPlainText(messageText);

                    // X-Msg-Ref
                    string msgRefValue;
                    if (message.TryGetProperty(MsgRefProperty, out msgRefValue))
                        m.MsgRef = msgRefValue;

                    message.IsRead = true;
                    message.Update(ConflictResolutionMode.AlwaysOverwrite);

                    // Add this message to list
                    list.Add(m);
                }
            }

            // Return the list of messages to handle
            return list;            
        }

        /// <summary>
        /// Archives a Mail Message to correct Year and Month Folder
        /// </summary>
        /// <param name="service">Exchange Web Service reference</param>
        /// <param name="Id">Mail Message Item Id</param>
        public string ArchiveMailMessage(MailQueueModel mqm)
        {
            var Id = mqm.Id;

            // Bind the message to get properties
            EmailMessage message = EmailMessage.Bind(_service, Id, new PropertySet(BasePropertySet.FirstClassProperties));

            // Find out Year and Month to archive on
            string year = message.DateTimeReceived.ToString("yyyy", CultureInfo.InvariantCulture);
            string month = message.DateTimeReceived.ToString("MM", CultureInfo.InvariantCulture);

            // Check if Year folder exists below Inbox
            FolderId yearFolderId = FindFolderId(WellKnownFolderName.Inbox, year);

            // Create folder for Year if it wasn't found
            if (yearFolderId == null)
            {
                Folder folder = new Folder(_service);
                folder.DisplayName = year;
                folder.Save(WellKnownFolderName.Inbox);
                yearFolderId = folder.Id;
            }

            // Check if Month folder exists below Year folder
            FolderId monthFolderId = FindFolderId(yearFolderId, month);

            // Create folder for Month if it wasn't found
            if (monthFolderId == null)
            {
                Folder folder = new Folder(_service);
                folder.DisplayName = month;
                folder.Save(yearFolderId);
                monthFolderId = folder.Id;
            }

            // Move the Mail Message to Month folder below Year folder
            message.Move(monthFolderId);

            // Return the FolderId which we moved to
            return monthFolderId.ToString();
        }

        /// <summary>
        /// Forward a mail message to another address
        /// </summary>
        /// <param name="service">Exchange Web Service Object</param>
        /// <param name="Id">Mail Message ItemId</param>
        /// <param name="recipientAddress">Receiving address</param>
        public void ForwardMailMessage(MailQueueModel mqm, string recipientAddress, string extraText = "", bool delete = true)
        {
            ItemId Id = mqm.Id;

            // Bind the message to get properties
            EmailMessage message = EmailMessage.Bind(_service, Id, new PropertySet(BasePropertySet.FirstClassProperties));

            // Create the prefixed content to add to the forwarded message body.
            string messageBodyPrefix = extraText == "" ? "Detta meddelande har vidarebefodrats av Chalmers.ILL för " + message.Sender.Name + " <" + message.Sender.Address + ">" : extraText;

            // Create the addresses that the forwarded email message is to be sent to.
            EmailAddress[] addresses = new EmailAddress[1];
            addresses[0] = new EmailAddress(recipientAddress);

            // Send the forwarded message
            message.Forward(messageBodyPrefix, addresses);

            if (delete)
            {
                // Delete the original message
                message.Delete(DeleteMode.MoveToDeletedItems);
            }
        }

        /// <summary>
        /// Sends an email message trough the Exchange Web Service API.
        /// </summary>
        /// <param name="service">Exchange Web Service Object.</param>
        /// <param name="orderId">The order id for the order which the email is regarding.</param>
        /// <param name="body">Message body.</param>
        /// <param name="subject">Message subject.</param>
        /// <param name="recipientName">The name of the recipient.</param>
        /// <param name="recipientAddress">Recipient address.</param>
        /// <param name="attachments">The attachments which should be sent out with the mail.</param>
        public void SendMailMessage(string orderId, string body, string subject, string recipientName, string recipientAddress, IDictionary<string, byte[]> attachments)
        {
            string senderEmail = ConfigurationManager.AppSettings["chalmersIllSenderAddress"];

            // Create an email message and identify the Exchange service.
            EmailMessage message = new EmailMessage(_service);

            // Add properties to the email message.
            message.Subject = subject + " #" + orderId;
            message.Body = new MessageBody(BodyType.HTML, body.Replace("\n", "<br />"));
            foreach (var address in recipientAddress.Split(','))
            {
                message.ToRecipients.Add(recipientName, address.Trim());
            }
            message.ReplyTo.Add(senderEmail);

            foreach (var attachment in attachments)
            {
                message.Attachments.AddFileAttachment(attachment.Key, attachment.Value);
            }

            // Send the email message and save a copy.
            message.SendAndSaveCopy();
        }

        /// <summary>
        /// Sends a plain email message trough the Exchange Web Service API
        /// </summary>
        /// <param name="service">Exchange Web Service Object</param>
        /// <param name="body">Message body</param>
        /// <param name="subject">Message subject</param>
        /// <param name="recipientAddress">Recipient address</param>
        public void SendPlainMailMessage(string body, string subject, string recipientAddress)
        {
            string senderEmail = ConfigurationManager.AppSettings["chalmersIllSenderAddress"];

            // Create an email message and identify the Exchange service.
            EmailMessage message = new EmailMessage(_service);

            // Add properties to the email message.
            message.Subject = subject;
            message.Body = new MessageBody(BodyType.HTML, body.Replace("\n", "<br />"));
            message.ToRecipients.Add(recipientAddress);
            message.ReplyTo.Add(senderEmail);

            // Send the email message and save a copy.
            message.Send();
        }

        public void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Find Exchange FolderId based on DisplayName of folder, starting from root
        /// </summary>
        /// <param name="service">Exchange Service object</param>
        /// <param name="folderDisplayName">The DisplayName to search for</param>
        /// <returns>Id of first folder found</returns>
        private FolderId FindFolderId(FolderId baseFolder, string folderDisplayName)
        {
            FolderView view = new FolderView(100);
            view.PropertySet = new PropertySet(BasePropertySet.IdOnly);
            view.PropertySet.Add(FolderSchema.DisplayName);
            view.Traversal = FolderTraversal.Shallow;
            FindFoldersResults findFolderResults = _service.FindFolders(baseFolder, view); // WellKnownFolderName.MsgFolderRoot
            foreach (Folder f in findFolderResults)
            {
                if (f.DisplayName == folderDisplayName)
                    return f.Id;
            }
            return null;
        }


        // Internal methods used when connecting to Exchange Web Service EWWS
        private bool RedirectionUrlValidationCallback(string redirectionUrl)
        {
            // The default for the validation callback is to reject the URL.
            bool result = false;

            Uri redirectionUri = new Uri(redirectionUrl);

            // Validate the contents of the redirection URL. In this simple validation
            // callback, the redirection URL is considered valid if it is using HTTPS
            // to encrypt the authentication credentials. 
            if (redirectionUri.Scheme == "https")
            {
                result = true;
            }
            return result;
        }

        private bool CertificateValidationCallBack(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            // If the certificate is a valid, signed certificate, return true.
            if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
            {
                return true;
            }

            // If there are errors in the certificate chain, look at each error to determine the cause.
            if ((sslPolicyErrors & System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                if (chain != null && chain.ChainStatus != null)
                {
                    foreach (System.Security.Cryptography.X509Certificates.X509ChainStatus status in chain.ChainStatus)
                    {
                        if ((certificate.Subject == certificate.Issuer) &&
                           (status.Status == System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.UntrustedRoot))
                        {
                            // Self-signed certificates with an untrusted root are valid. 
                            continue;
                        }
                        else
                        {
                            if (status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.NoError)
                            {
                                // If there are any other errors in the certificate chain, the certificate is invalid,
                                // so the method returns false.
                                return false;
                            }
                        }
                    }
                }

                // When processing reaches this line, the only errors in the certificate chain are 
                // untrusted root errors for self-signed certificates. These certificates are valid
                // for default Exchange server installations, so return true.
                return true;
            }
            else
            {
                // In all other cases, return false.
                return false;
            }
        }

        private SecureString getPassword()
        {
            SecureString pwd = new SecureString();
            while (true)
            {
                ConsoleKeyInfo i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (i.Key == ConsoleKey.Backspace)
                {
                    if (pwd.Length > 0)
                    {
                        pwd.RemoveAt(pwd.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else
                {
                    pwd.AppendChar(i.KeyChar);
                    Console.Write("*");
                }
            }
            return pwd;
        }

        private string convertToUNSecureString(SecureString secstrPassword)
        {
            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secstrPassword);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }
    }
}