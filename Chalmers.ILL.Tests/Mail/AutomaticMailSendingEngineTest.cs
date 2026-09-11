using Chalmers.ILL.Mail;
using Chalmers.ILL.Templates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Mail;
using Chalmers.ILL.OrderItems;
using static Chalmers.ILL.Models.OrderItemModel;

namespace Chalmers.ILL.Tests.Mail
{
    [TestClass]
    public class AutomaticMailSendingEngineTest
    {
        private IOrderItemSearcher GetFakeSearcher(IEnumerable<OrderItemModel> fakeSearchResults)
        {
            return new StubOrderItemSearcher(fakeSearchResults);
        }

        private IAutomaticMailSendingEngine SetupAutomaticMailSendingEngine(string status, DateTime dueDate, DateTime deliveryDate, AutomaticMailSendTestResult result)
        {
            var fakeSearchResults = new List<OrderItemModel>()
            {
                new OrderItemModel()
                {
                    Status = status,
                    OrderId = "cth-123",
                    PatronName = "John Doe",
                    PatronEmail = "john@doe.com",
                    DueDate = dueDate,
                    DeliveryDate = deliveryDate
                }
            };

            IOrderItemSearcher orderItemsSearcher = GetFakeSearcher(fakeSearchResults);

            ITemplateService templateService = new StubTemplateService((nodeName, orderItem) =>
            {
                result.MailTemplate = nodeName;
                return "FEJKMAIL";
            });

            IOrderItemManager orderItemManager = new StubOrderItemManager
            {
                OnSetStatus = (nodeId, statusPrevalue, eventId, doReindex, doSignal) =>
                {
                    result.NewStatus = statusPrevalue;

                    if (doReindex)
                    {
                        result.NumberOfReindexes++;
                    }

                    if (doSignal)
                    {
                        result.NumberOfSignals++;
                    }
                },
                OnAddLogItem = (nodeId, type, msg, eventId, doReindex, doSignal) =>
                {
                    result.NumberOfLogMessages++;

                    if (doReindex)
                    {
                        result.NumberOfReindexes++;
                    }

                    if (doSignal)
                    {
                        result.NumberOfSignals++;
                    }
                }
            };

            IMailService mailService = new StubMailService((mailModel) =>
            {
                Assert.AreEqual("cth-123", mailModel.OrderId, "The order id was not as expected.");
                Assert.AreEqual("John Doe", mailModel.recipientName, "The recipient name was not as expected.");
                Assert.AreEqual("john@doe.com", mailModel.recipientEmail, "The recipient email address was not as expected.");
                Assert.AreEqual("FEJKMAIL", mailModel.message, "The sent email message was not as expected.");
            });

            return new AutomaticMailSendingEngine(orderItemsSearcher, templateService, orderItemManager, mailService);
        }

        private class AutomaticMailSendTestResult
        {
            public int NumberOfLogMessages { get; set; }
            public int NumberOfReindexes { get; set; }
            public int NumberOfSignals { get; set; }
            public string MailTemplate { get; set; }
            public string NewStatus { get; set; }
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateIsInFiveDays_CourtesyNoticeIsSentOut()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now.AddDays(5), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(2, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("CourtesyNoticeMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateWasYesterday_LoanPeriodOverMailIsSentOut()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now.AddDays(-1), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(2, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("LoanPeriodOverMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateWasFiveDaysAgo_LoanPeriodReallyOverMailIsSentOut()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now.AddDays(-5), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(2, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("LoanPeriodReallyOverMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateWasTenDaysAgo_LoanPeriodReallyReallyOverMailIsSentOut()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now.AddDays(-10), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(2, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("LoanPeriodReallyReallyOverMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateIsToday_NothingHappens()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now, new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateIsFarIntoTheFuture_NothingHappens()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now.AddDays(24), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_OnLoanDueDateHasPassedLongAgo_StatusDoSomething()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("11:Utlånad", DateTime.Now.AddDays(-24), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(1, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual("02:Åtgärda", result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateIsInFiveDays_NothingHappens()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now.AddDays(5), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateWasYesterday_NothingHappens()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now.AddDays(-1), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateWasFiveDaysAgo_LoanPeriodReallyOverMailIsSentOut()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now.AddDays(-5), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(2, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("LoanPeriodReallyOverMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateWasTenDaysAgo_LoanPeriodReallyReallyOverMailIsSentOut()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now.AddDays(-10), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(2, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("LoanPeriodReallyReallyOverMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateIsToday_NothingHappens()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now, new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateIsFarIntoTheFuture_NothingHappens()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now.AddDays(24), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_ClaimedDueDateHasPassedLongAgo_StatusDoSomething()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("12:Krävd", DateTime.Now.AddDays(-24), new DateTime(1970, 1, 1), result).SendOutMailsThatAreDue();

            Assert.AreEqual(1, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual("02:Åtgärda", result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_InTransitDeliveryDateMoreThanFourBusinessDaysAgo_StatusDelivered()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("13:Transport", new DateTime(1970, 1, 1), DateTime.Now.AddDays(-7), result).SendOutMailsThatAreDue();

            Assert.AreEqual(3, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(1, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(1, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual("ArticleAvailableInInfodiskMailTemplate", result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual("05:Levererad", result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void SendOutMailsThatAreDue_InTransitDeliveryDateLessThanFourBusinessDaysAgo_StatusDelivered()
        {
            var result = new AutomaticMailSendTestResult();

            SetupAutomaticMailSendingEngine("13:Transport", new DateTime(1970, 1, 1), DateTime.Now.AddDays(-3), result).SendOutMailsThatAreDue();

            Assert.AreEqual(0, result.NumberOfLogMessages, "Number of messages logged was not as expected.");
            Assert.AreEqual(0, result.NumberOfReindexes, "Number of reindexes was not as expected.");
            Assert.AreEqual(0, result.NumberOfSignals, "Number of signals was not as expected.");
            Assert.AreEqual(null, result.MailTemplate, "The fetched template was not as expected.");
            Assert.AreEqual(null, result.NewStatus, "The new status was not as expected.");
        }

        [TestMethod]
        public void AddBusinessDays_FromMondayAddFourDays_ThursdaySameWeek()
        {
            var startDate = new DateTime(2023, 5, 15); // Monday
            var endDate = AutomaticMailSendingEngine.AddBusinessDays(startDate, 4);

            Assert.AreEqual(endDate.Year, 2023);
            Assert.AreEqual(endDate.Month, 5);
            Assert.AreEqual(endDate.Day, 19);
        }

        [TestMethod]
        public void AddBusinessDays_FromMondayAddSevenDays_WednesdayNextWeek()
        {
            var startDate = new DateTime(2023, 5, 15); // Monday
            var endDate = AutomaticMailSendingEngine.AddBusinessDays(startDate, 7);

            Assert.AreEqual(endDate.Year, 2023);
            Assert.AreEqual(endDate.Month, 5);
            Assert.AreEqual(endDate.Day, 24);
        }

        [TestMethod]
        public void AddBusinessDays_FromSaturdayAddSevenDays_WednesdayNextNextWeek()
        {
            var startDate = new DateTime(2023, 5, 13); // Saturday
            var endDate = AutomaticMailSendingEngine.AddBusinessDays(startDate, 7);

            Assert.AreEqual(endDate.Year, 2023);
            Assert.AreEqual(endDate.Month, 5);
            Assert.AreEqual(endDate.Day, 24);
        }

        [TestMethod]
        public void AddBusinessDays_FromFridayAddSevenDays_TuedayNextNextWeek()
        {
            var startDate = new DateTime(2023, 5, 12); // Friday
            var endDate = AutomaticMailSendingEngine.AddBusinessDays(startDate, 7);

            Assert.AreEqual(endDate.Year, 2023);
            Assert.AreEqual(endDate.Month, 5);
            Assert.AreEqual(endDate.Day, 23);
        }

        [TestMethod]
        public void AddBusinessDays_FromSundayAddSevenDays_FridayNextWeek()
        {
            var startDate = new DateTime(2023, 5, 14); // Sunday
            var endDate = AutomaticMailSendingEngine.AddBusinessDays(startDate, 4);

            Assert.AreEqual(endDate.Year, 2023);
            Assert.AreEqual(endDate.Month, 5);
            Assert.AreEqual(endDate.Day, 19);
        }

        class StubOrderItemSearcher : IOrderItemSearcher
        {
            private readonly IEnumerable<OrderItemModel> _results;

            public StubOrderItemSearcher(IEnumerable<OrderItemModel> results)
            {
                _results = results;
            }

            public IEnumerable<OrderItemModel> Search(string query) => _results;
            public SearchResult Search(string query, int start, int size) => throw new NotImplementedException();
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => throw new NotImplementedException();
            public IEnumerable<string> AggregatedProviders() => throw new NotImplementedException();
            public void Added(OrderItemModel item) { }
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }

        class StubTemplateService : ITemplateService
        {
            private readonly Func<string, OrderItemModel, string> _getTemplateData;

            public StubTemplateService(Func<string, OrderItemModel, string> getTemplateData)
            {
                _getTemplateData = getTemplateData;
            }

            public IList<Template> GetManualTemplates() => throw new NotImplementedException();
            public string GetTemplateData(int nodeId) => throw new NotImplementedException();
            public string GetTemplateData(string nodeName) => throw new NotImplementedException();
            public string GetTemplateData(string nodeName, OrderItemModel orderItem) => _getTemplateData(nodeName, orderItem);
            public string GetTemplateData(int templateId, OrderItemModel orderItem) => throw new NotImplementedException();
            public void SetTemplateData(int nodeId, string data) => throw new NotImplementedException();
            public void CreateTemplate(string description, bool acquisition) => throw new NotImplementedException();
            public List<Template> PopulateTemplateList(List<Template> list) => throw new NotImplementedException();
            public string GetPrettyLibraryNameFromLibraryAbbreviation(string libraryName) => throw new NotImplementedException();
            public string ReplaceMoustaches(string templateName, string templateString, OrderItemModel orderItem) => throw new NotImplementedException();
        }

        class StubMailService : IMailService
        {
            private readonly Action<OutgoingMailModel> _sendMail;

            public StubMailService(Action<OutgoingMailModel> sendMail)
            {
                _sendMail = sendMail;
            }

            public void SendMail(OutgoingMailModel mailModel) => _sendMail(mailModel);
            public void DeleteOldMessagesFromFolder(string folder, DateTime oldLimit) { }
        }

        class StubOrderItemManager : IOrderItemManager
        {
            public Action<int, string, string, bool, bool> OnSetStatus;
            public Action<int, string, string, string, bool, bool> OnAddLogItem;

            public OrderItemModel GetOrderItem(int nodeId) => null;
            public OrderItemModel GetOrderItem(string orderId) => throw new NotImplementedException();
            public IEnumerable<OrderItemModel> GetLockedOrderItems(string memberId) => throw new NotImplementedException();
            public List<LogItem> GetLogItems(int nodeId) => throw new NotImplementedException();
            public string GenerateEventId(int type) => "evt";
            public int CreateOrderItemInDbFromMailQueueModel(MailQueueModel model, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public int CreateOrderItemInDbFromOrderItemSeedModel(OrderItemSeedModel model, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public int CreateOrderItemInDbFromOrderItemModel(OrderItemModel model, bool doReindex = true, bool doSignal = true) => throw new NotImplementedException();
            public void SaveWithoutEventsAndWithSynchronousReindexing(int nodeId, bool doReindex = true, bool doSignal = true) { }
            public void AddExistingMediaItemAsAnAttachment(int orderNodeId, string mediaNodeId, string title, string link, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AddExistingMediaItemAsAnAttachmentWithoutLogging(int orderNodeId, string mediaNodeId, string title, string link, bool doReindex = true, bool doSignal = true) { }
            public void AddLogItem(int OrderItemNodeId, string Type, string Message, string eventId, bool doReindex = true, bool doSignal = true)
                => OnAddLogItem?.Invoke(OrderItemNodeId, Type, Message, eventId, doReindex, doSignal);
            public void AddSierraDataToLog(int orderItemNodeId, SierraModel sm, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void RemoveConnectionToMediaItem(int orderNodeId, string mediaNodeId, bool doReindex = true, bool doSignal = true) { }
            public void SetFollowUpDateWithoutLogging(int nodeId, DateTime date, bool doReindex = true, bool doSignal = true) { }
            public void SetDrmWarningWithoutLogging(int orderNodeId, bool status, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderNameWithoutLogging(int nodeId, string providerName, bool doReindex = true, bool doSignal = true) { }
            public void SetFollowUpDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderDueDate(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDeliveryDateWithoutLogging(int nodeId, DateTime date, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetCancellationReason(int orderNodeId, int cancellationReasonId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDeliveryLibrary(int orderNodeId, int deliveryLibraryId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetPurchaseLibrary(int orderNodeId, PurchaseLibraries library, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDeliveryLibrary(int orderNodeId, string deliveryLibraryPrevalue, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetDrmWarning(int orderNodeId, bool status, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetPurchasedMaterial(int orderNodeId, int purchasedMaterialId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetStatus(int orderNodeId, int statusId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetStatus(int orderNodeId, string statusPrevalue, string eventId, bool doReindex = true, bool doSignal = true)
                => OnSetStatus?.Invoke(orderNodeId, statusPrevalue, eventId, doReindex, doSignal);
            public void SetType(int orderNodeId, int typeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetBookId(int nodeId, string bookId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetPatronData(int nodeId, string sierraInfo, int sierraPatronRecordId, int pType, string homeLibrary, string aff, bool doReindex = true, bool doSignal = true) { }
            public void SetPatronEmail(int nodeId, string email, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderName(int nodeId, string providerName, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderOrderId(int nodeId, string providerOrderId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetProviderInformation(int nodeId, string providerInformation, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetReference(int nodeId, string reference, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SilentAnonymization(int nodeId, string reference, IList<LogItem> logs, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetReadOnlyAtLibrary(int nodeId, bool readOnlyAtLibrary, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetEditedByData(int orderNodeId, string memberId, string memberName, bool doReindex = true, bool doSignal = true) { }
            public void SetTitleInformation(int nodeId, string titleInformation, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void AnonymizeOrder(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void MakeDuplicate(int orderNodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void SetIsAnonymized(int nodeId, bool isAnonymized, string eventId, bool doReindex = true, bool doSignal = true) { }
            public void ResetAllAnonymizationFlags(int nodeId, string eventId, bool doReindex = true, bool doSignal = true) { }
        }
    }
}
