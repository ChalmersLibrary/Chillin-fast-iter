using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers.Page;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.Page;
using Chalmers.ILL.OrderItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class PageControllersTest
    {
        [TestMethod]
        public void ChalmersILLController_Index_ReturnsViewWithModel()
        {
            var controller = new ChalmersILLController(new StubMemberInfoManager());
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(ChalmersILLModel));
        }

        [TestMethod]
        public void ChalmersILLController_Index_DoesNotReturnLayoutViewDirectly()
        {
            // ChalmersILL.cshtml is the shared layout (it calls @RenderBody()) and throws
            // "cannot be requested directly because it calls the RenderBody method" if returned
            // as the response view instead of being used via Layout = "ChalmersILL.cshtml".
            var controller = new ChalmersILLController(new StubMemberInfoManager());
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.AreNotEqual("~/Views/ChalmersILL.cshtml", result.ViewName);
        }

        [TestMethod]
        public void ChalmersILLController_Index_PopulatesMemberData()
        {
            var memberManager = new StubMemberInfoManager();
            var controller = new ChalmersILLController(memberManager);
            SetHttpContext(controller);

            controller.Index();

            Assert.IsTrue(memberManager.PopulateWasCalled);
        }

        [TestMethod]
        public void ChalmersILLSettingsPageController_Index_ReturnsViewWithModel()
        {
            var controller = new ChalmersILLSettingsPageController(new StubMemberInfoManager());
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(ChalmersILLSettingsPageModel));
        }

        [TestMethod]
        public void ChalmersILLStartPageController_Index_ReturnsViewWithModel()
        {
            var controller = new ChalmersILLStartPageController(new StubMemberInfoManager());
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(ChalmersILLStartPageModel));
        }

        [TestMethod]
        public void ChalmersILLStatisticsPageController_Index_ReturnsViewWithModel()
        {
            var controller = new ChalmersILLStatisticsPageController(new StubMemberInfoManager());
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(ChalmersILLStatisticsPageModel));
        }

        [TestMethod]
        public async System.Threading.Tasks.Task ChalmersILLLogoutPageController_Index_WhenNotAuthenticated_ReturnsViewWithoutSigningOut()
        {
            var memberManager = new StubMemberInfoManager();
            var controller = new ChalmersILLLogoutPageController(memberManager);
            SetHttpContext(controller);

            var result = await controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(ChalmersILLLogoutPageModel));
            Assert.IsFalse(memberManager.ClearCacheWasCalled);
        }

        [TestMethod]
        public void ChalmersILLDiskPageController_Index_WithoutQuery_DoesNotSearch()
        {
            var searcher = new StubOrderItemSearcher();
            var controller = new ChalmersILLDiskPageController(new StubMemberInfoManager(), searcher);
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;
            var model = result.Model as ChalmersILLDiskPageModel;

            Assert.IsNotNull(model);
            Assert.IsFalse(searcher.SearchWasCalled);
        }

        [TestMethod]
        public void ChalmersILLDiskPageController_Index_WithQuery_SearchesOrders()
        {
            var searcher = new StubOrderItemSearcher();
            var controller = new ChalmersILLDiskPageController(new StubMemberInfoManager(), searcher);
            SetHttpContext(controller, "query=cthb-abc");

            var result = controller.Index() as ViewResult;
            var model = result.Model as ChalmersILLDiskPageModel;

            Assert.IsNotNull(model);
            Assert.IsTrue(searcher.SearchWasCalled);
        }

        [TestMethod]
        public void ChalmersILLOrderListPageController_Index_WithoutQuery_SearchesDefaultItems()
        {
            var searcher = new StubOrderItemSearcher();
            var controller = new ChalmersILLOrderListPageController(new StubMemberInfoManager(), searcher);
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;
            var model = result.Model as ChalmersILLOrderListPageModel;

            Assert.IsNotNull(model);
            Assert.IsNotNull(model.PendingOrderItems);
            Assert.IsTrue(searcher.SearchWithPaginationWasCalled);
        }

        [TestMethod]
        public void ChalmersILLOrderListPageController_Index_WithOrderIdQuery_QuotesQuery()
        {
            var searcher = new StubOrderItemSearcher();
            var controller = new ChalmersILLOrderListPageController(new StubMemberInfoManager(), searcher);
            SetHttpContext(controller, "query=cthb-abc12345-1");

            controller.Index();

            Assert.IsTrue(searcher.LastSearchQuery.StartsWith("\"cthb-"));
        }

        [TestMethod]
        public void ChalmersILLOrderListPageController_Index_WithFreeTextQuery_DoesNotQuoteQuery()
        {
            var searcher = new StubOrderItemSearcher();
            var controller = new ChalmersILLOrderListPageController(new StubMemberInfoManager(), searcher);
            SetHttpContext(controller, "query=some+text");

            controller.Index();

            Assert.IsFalse(searcher.LastSearchQuery.StartsWith("\""));
        }

        [TestMethod]
        public void ChalmersILLLoginPageController_Index_ReturnsView()
        {
            var controller = new ChalmersILLLoginPageController();
            SetHttpContext(controller);

            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
        }

        private static void SetHttpContext(Controller controller, string queryString = "")
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };
            httpContext.Request.Path = "/";
            if (!string.IsNullOrEmpty(queryString))
                httpContext.Request.QueryString = new QueryString("?" + queryString);
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        }

        class StubMemberInfoManager : IMemberInfoManager
        {
            public bool PopulateWasCalled { get; private set; }
            public bool ClearCacheWasCalled { get; private set; }
            public int GetCurrentMemberId(HttpRequest request, HttpResponse response) => 0;
            public string GetCurrentMemberText(HttpRequest request, HttpResponse response) => "";
            public string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response) => "";
            public void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, ChalmersILLModel model) { PopulateWasCalled = true; }
            public void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName) { }
            public void ClearMemberCache(HttpResponse response) { ClearCacheWasCalled = true; }
        }

        class StubOrderItemSearcher : IOrderItemSearcher
        {
            public bool SearchWasCalled { get; private set; }
            public bool SearchWithPaginationWasCalled { get; private set; }
            public string LastSearchQuery { get; private set; }

            public IEnumerable<OrderItemModel> Search(string query)
            {
                SearchWasCalled = true;
                LastSearchQuery = query;
                return new List<OrderItemModel>();
            }

            public SearchResult Search(string query, int start, int size)
            {
                SearchWithPaginationWasCalled = true;
                LastSearchQuery = query;
                return new SearchResult { Items = new List<OrderItemModel>(), Count = 0 };
            }

            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => new List<OrderItemModel>();
            public IEnumerable<string> AggregatedProviders() => new List<string>();
            public void Added(OrderItemModel item) { }
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }
    }
}
