using System;
using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Mail;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.Tests.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    // The cron jobs while the search index is being rebuilt in the background: they'd only see
    // part of the orders, so they wait for it instead.
    [TestClass]
    public class SystemSurfaceControllerTest
    {
        private static SystemSurfaceController NewController(IOrderItemSearcher searcher, IAutomaticMailSendingEngine mail)
        {
            var controller = new SystemSurfaceController(null, null, null, null, null, searcher, mail, new StubChillinConfiguration());
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("localhost");
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            return controller;
        }

        [TestMethod]
        public void Update_WhileTheIndexIsRebuilt_SearchesNothing()
        {
            var searcher = new RebuildingSearcher();

            var result = (JsonResult)NewController(searcher, new RecordingMailEngine()).Update();

            Assert.AreEqual(0, searcher.Searches);
            Assert.AreEqual(0, ((List<SourcePollingResult>)result.Value).Count);
        }

        [TestMethod]
        public void SendOutAutomaticMails_WhileTheIndexIsRebuilt_SendsNothingAndSaysWhy()
        {
            var mail = new RecordingMailEngine();

            var result = (JsonResult)NewController(new RebuildingSearcher(), mail).SendOutAutomaticMailsThatAreDue();
            var response = (ResultResponseWithData)result.Value;

            Assert.AreEqual(0, mail.SendCalls);
            Assert.IsFalse(response.Success);
            StringAssert.Contains(response.Message, "Sökindexet byggs om");
        }

        private sealed class RebuildingSearcher : IOrderItemSearcher
        {
            public int Searches;

            public SearchIndexStatus Status => new SearchIndexStatus { Rebuilding = true, Indexed = 10, Total = 100 };

            public IEnumerable<OrderItemModel> Search(string query) { Searches++; return Enumerable.Empty<OrderItemModel>(); }
            public SearchResult Search(string query, int start, int size) { Searches++; return new SearchResult { Items = new List<OrderItemModel>() }; }
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) { Searches++; return Enumerable.Empty<OrderItemModel>(); }
            public IEnumerable<string> AggregatedProviders() => throw new NotSupportedException();
            public void Added(OrderItemModel item) => throw new NotSupportedException();
            public void Modified(OrderItemModel item) => throw new NotSupportedException();
            public void Deleted(OrderItemModel item) => throw new NotSupportedException();
            public int Rebuild(IEnumerable<OrderItemModel> items) => throw new NotSupportedException();
        }

        private sealed class RecordingMailEngine : IAutomaticMailSendingEngine
        {
            public int SendCalls;

            public IEnumerable<MailOperationResult> SendOutMailsThatAreDue() { SendCalls++; return new List<MailOperationResult>(); }
            public void RemoveOldSentMails() { }
        }
    }
}
