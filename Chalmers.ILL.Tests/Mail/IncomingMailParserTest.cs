using Chalmers.ILL.Mail;
using Chalmers.ILL.Models.Mail;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Mail
{
    // Characterization test for the parsing logic extracted from
    // MicrosoftGraphMailWebApi.ReadMailQueue into IncomingMailParser (fas 6, isolerat läge steg A) -
    // pins the exact HTML shape OrderItemMailSurfaceController.SendMailForNewOrder produces
    // (a hidden "chalmers.ill.orderitem" div), so both the real Graph implementation and
    // Isolated.FileMailWebApi are proven to interpret order-submission mail the same way.
    [TestClass]
    public class IncomingMailParserTest
    {
        private const string OrderMailBody =
            "<div id='chalmers.ill.orderitem'>\n" +
            "<div id='OriginalOrder'>Please order this article for me</div>\n" +
            "<div id='PatronName'>Test Testsson</div>\n" +
            "<div id='PatronEmail'>test.testsson@example.com</div>\n" +
            "<div id='PatronCardNo'>1234567890</div>\n" +
            "<div id='Purchase'>False</div>\n" +
            "<div id='DeliveryLibrary'>hbib</div>\n" +
            "</div>\n";

        [TestMethod]
        public void ParseIncomingMailBody_OrderItemDivPresent_PopulatesOrderFields()
        {
            var m = new MailQueueModel();

            IncomingMailParser.ParseIncomingMailBody(m, OrderMailBody);

            Assert.AreEqual("Please order this article for me", m.OriginalOrder);
            Assert.AreEqual("Test Testsson", m.PatronName);
            Assert.AreEqual("test.testsson@example.com", m.PatronEmail);
            Assert.AreEqual("1234567890", m.PatronCardNo);
            Assert.IsFalse(m.IsPurchaseRequest);
            Assert.AreEqual("hbib", m.DeliveryLibrary);
        }

        [TestMethod]
        public void ParseIncomingMailBody_PurchaseTrue_SetsIsPurchaseRequest()
        {
            var m = new MailQueueModel();
            var body = OrderMailBody.Replace("<div id='Purchase'>False</div>", "<div id='Purchase'>True</div>");

            IncomingMailParser.ParseIncomingMailBody(m, body);

            Assert.IsTrue(m.IsPurchaseRequest);
        }

        [TestMethod]
        public void ParseIncomingMailBody_NoOrderItemDiv_LeavesOrderFieldsUnset_ButStillSetsMessageBody()
        {
            var m = new MailQueueModel();

            IncomingMailParser.ParseIncomingMailBody(m, "<p>Just a plain reply, no order data here.</p>");

            Assert.IsNull(m.OriginalOrder);
            Assert.IsNull(m.PatronName);
            Assert.IsFalse(m.IsPurchaseRequest);
            StringAssert.Contains(m.MessageBody, "Just a plain reply, no order data here.");
        }

        [TestMethod]
        public void ParseIncomingMailBody_OrderItemDivPresent_AlsoSetsPlainTextMessageBody()
        {
            var m = new MailQueueModel();

            IncomingMailParser.ParseIncomingMailBody(m, OrderMailBody);

            StringAssert.Contains(m.MessageBody, "Please order this article for me");
        }
    }
}
