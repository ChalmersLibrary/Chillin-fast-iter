using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Chalmers.ILL.Controllers.SurfaceControllers;
using System.Collections.Generic;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Tests.OrderItems
{
    [TestClass]
    public class BulkDataManagerTest
    {
        private IOrderItemSearcher CreateFakeSearcher(IEnumerable<OrderItemModel> searchResults)
        {
            return new StubOrderItemSearcher(searchResults);
        }

        private bool IsAsExpected(SimplifiedOrderItem soi, string type, string reference, string status)
        {
            return soi.Type == type && soi.Reference == reference && soi.Status == status;
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneArticleOrderedSv_ArticleNotAvailableYet()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-3),
                            DeliveryDate = new DateTime(1970, 1, 1),
                            Type = "Artikel",
                            Status = "03:Beställd",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Artikel", "Mio min Mio.", "Ej tillgänglig än"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneArticleOrderedAndReadyThreeDaysAgoSv_ArticleDelivered()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-3),
                            DeliveryDate = new DateTime(1970, 1, 1),
                            Type = "Artikel",
                            Status = "05:Levererad",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Artikel", "Mio min Mio.", "Levererad"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookOrderedSv_BookNotAvailableYet()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-1),
                            DeliveryDate = new DateTime(1970, 1, 1),
                            Type = "Bok",
                            Status = "01:Ny",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Bok", "Mio min Mio.", "Ej tillgänglig än"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookRetrievedSv_DuePlusDate()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(30),
                            DeliveryDate = DateTime.Now.AddDays(-1),
                            Type = "Bok",
                            Status = "11:Utlånad",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Bok", "Mio min Mio.", "ÅTER " + DateTime.Now.AddDays(30).ToString("yyyy-MM-dd")));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookRecalledSv_Recalled()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(12),
                            DeliveryDate = DateTime.Now.AddDays(-6),
                            Type = "Bok",
                            Status = "12:Krävd",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Bok", "Mio min Mio.", "Krävd *"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookLateSv_Late()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-1),
                            DeliveryDate = DateTime.Now.AddDays(-32),
                            Type = "Bok",
                            Status = "11:Utlånad",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Bok", "Mio min Mio.", "Försenad **"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookRetrievedMailContactSv_DuePlusDate()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(30),
                            DeliveryDate = DateTime.Now.AddDays(-1),
                            Type = "Bok",
                            Status = "02:Åtgärda",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Bok", "Mio min Mio.", "ÅTER " + DateTime.Now.AddDays(30).ToString("yyyy-MM-dd")));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookLateMailContactSv_Late()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-1),
                            DeliveryDate = DateTime.Now.AddDays(-32),
                            Type = "Bok",
                            Status = "02:Åtgärda",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "sv");
            Assert.IsTrue(IsAsExpected(res[0], "Bok", "Mio min Mio.", "Försenad **"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneArticleOrderedEn_ArticleNotAvailableYet()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-3),
                            DeliveryDate = new DateTime(1970, 1, 1),
                            Type = "Artikel",
                            Status = "03:Beställd",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Article", "Mio min Mio.", "Not available yet"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneArticleOrderedAndReadyThreeDaysAgoEn_ArticleDelivered()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-3),
                            DeliveryDate = new DateTime(1970, 1, 1),
                            Type = "Artikel",
                            Status = "05:Levererad",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Article", "Mio min Mio.", "Delivered"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookOrderedEn_BookNotAvailableYet()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-1),
                            DeliveryDate = new DateTime(1970, 1, 1),
                            Type = "Bok",
                            Status = "01:Ny",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Book", "Mio min Mio.", "Not available yet"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookRetrievedEn_DuePlusDate()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(30),
                            DeliveryDate = DateTime.Now.AddDays(-1),
                            Type = "Bok",
                            Status = "11:Utlånad",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Book", "Mio min Mio.", "DUE " + DateTime.Now.AddDays(30).ToString("yyyy-MM-dd")));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookRecalledEn_Recalled()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(12),
                            DeliveryDate = DateTime.Now.AddDays(-6),
                            Type = "Bok",
                            Status = "12:Krävd",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Book", "Mio min Mio.", "Recalled *"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookLateEn_Late()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-1),
                            DeliveryDate = DateTime.Now.AddDays(-32),
                            Type = "Bok",
                            Status = "11:Utlånad",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Book", "Mio min Mio.", "Late **"));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookRetrievedMailContactEn_DuePlusDate()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(30),
                            DeliveryDate = DateTime.Now.AddDays(-1),
                            Type = "Bok",
                            Status = "02:Åtgärda",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Book", "Mio min Mio.", "DUE " + DateTime.Now.AddDays(30).ToString("yyyy-MM-dd")));
        }

        [TestMethod]
        public void GetChillinDataForSierraPatron_OneBookLateMailContactEn_Late()
        {
            var bulkDataManager = new BulkDataManager(CreateFakeSearcher(
                    new List<OrderItemModel>()
                    {
                        new OrderItemModel()
                        {
                            DueDate = DateTime.Now.AddDays(-1),
                            DeliveryDate = DateTime.Now.AddDays(-32),
                            Type = "Bok",
                            Status = "02:Åtgärda",
                            OriginalOrder = "Mio min Mio."
                        }
                    }
                ));

            var res = bulkDataManager.GetChillinDataForSierraPatron(42, "en");
            Assert.IsTrue(IsAsExpected(res[0], "Book", "Mio min Mio.", "Late **"));
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
    }
}
