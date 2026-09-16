using System.Collections.Generic;
using System.Text;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.Utilities;
using HtmlAgilityPack;

namespace Chalmers.ILL.Mail
{
    // Extracted from MicrosoftGraphMailWebApi.ReadMailQueue (fas 6, isolerat läge steg A) so that
    // Isolated.FileMailWebApi can parse simulated incoming order mail with the exact same rules as
    // the real Graph implementation, instead of only exercising the transport and never the part
    // that actually breaks (the HTML parsing). Order-submission mail embeds its structured fields
    // as a hidden "chalmers.ill.orderitem" div (see OrderItemMailSurfaceController.SendMailForNewOrder)
    // - a reply that doesn't have one is a plain incoming message, not an order.
    public static class IncomingMailParser
    {
        public static void ParseIncomingMailBody(MailQueueModel mailQueueModel, string htmlBodyContent)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(MailBodyFixer.RemoveHtmlAroundLinks(htmlBodyContent.Replace("<br>", "<br>\n")));

            if (htmlDoc.DocumentNode.HasChildNodes && htmlDoc.GetElementbyId("chalmers.ill.orderitem") != null)
            {
                mailQueueModel.PatronName = htmlDoc.GetElementbyId("PatronName").InnerText;
                mailQueueModel.PatronEmail = htmlDoc.GetElementbyId("PatronEmail").InnerText;
                mailQueueModel.PatronCardNo = htmlDoc.GetElementbyId("PatronCardNo").InnerText;
                mailQueueModel.OriginalOrder = htmlDoc.GetElementbyId("OriginalOrder").InnerText;
                if (htmlDoc.GetElementbyId("Purchase") != null)
                {
                    mailQueueModel.IsPurchaseRequest = System.Convert.ToBoolean(htmlDoc.GetElementbyId("Purchase").InnerText);
                }
                else
                {
                    mailQueueModel.IsPurchaseRequest = false;
                }
                mailQueueModel.DeliveryLibrary = htmlDoc.GetElementbyId("DeliveryLibrary").InnerText;
            }

            // Message body as text only
            var sb = new StringBuilder();

            if (htmlDoc.DocumentNode != null)
            {
                var textNodes = htmlDoc.DocumentNode.SelectNodes("//text()");
                if (textNodes != null)
                {
                    foreach (HtmlNode node in textNodes)
                    {
                        if (!node.HasChildNodes)
                        {
                            var text = node.InnerText;
                            if (!string.IsNullOrEmpty(text))
                                sb.AppendLine(text.Trim());
                        }
                    }
                }
            }

            mailQueueModel.MessageBody = Helpers.HtmlToPlainText(sb.ToString());
        }
    }
}
