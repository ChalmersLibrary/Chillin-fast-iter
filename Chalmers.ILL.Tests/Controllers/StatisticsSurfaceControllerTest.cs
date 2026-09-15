using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class StatisticsSurfaceControllerTest
    {
        [TestMethod]
        public void GetAvailableValues_ReturnsDistinctSortedValuesPerKey()
        {
            var orders = new List<OrderItemModel>
            {
                new OrderItemModel { Type = "Artikel", ProviderName = "Subito", SierraInfo = new SierraModel { ptype = 1, home_library = "z" } },
                new OrderItemModel { Type = "Bok", ProviderName = "Subito", SierraInfo = new SierraModel { ptype = 2, home_library = "zl" } },
                new OrderItemModel { Type = "Artikel", ProviderName = "Libris", SierraInfo = new SierraModel { ptype = 1, home_library = "za" } }
            };
            var controller = new StatisticsSurfaceController(new StubOrderItemSearcher(orders));

            var result = controller.GetAvailableValues(new KeyValueRequest
            {
                Keys = new List<string> { "Type", "ProviderName", "pType", "HomeLibrary" }
            }) as JsonResult;
            var response = result?.Value as KeyValueResult;

            Assert.IsNotNull(response);
            Assert.IsTrue(response.Success);

            var typeValues = response.KeyValues.Find(kv => kv.Key == "Type");
            Assert.AreEqual("Typ", typeValues.Name);
            CollectionAssert.AreEqual(new List<string> { "Artikel", "Bok" }, typeValues.AvailableValues);

            var providerValues = response.KeyValues.Find(kv => kv.Key == "ProviderName");
            Assert.AreEqual("Leverantörsnamn", providerValues.Name);
            CollectionAssert.AreEqual(new List<string> { "Libris", "Subito" }, providerValues.AvailableValues);

            var pTypeValues = response.KeyValues.Find(kv => kv.Key == "pType");
            Assert.AreEqual("P-Typ", pTypeValues.Name);
            CollectionAssert.AreEqual(new List<string> { "1", "2" }, pTypeValues.AvailableValues);

            var homeLibraryValues = response.KeyValues.Find(kv => kv.Key == "HomeLibrary");
            Assert.AreEqual("Hembibliotek", homeLibraryValues.Name);
            CollectionAssert.AreEqual(new List<string> { "z", "za", "zl" }, homeLibraryValues.AvailableValues);
        }

        [TestMethod]
        public void GetAvailableValues_SearcherThrows_ReturnsFailureWithoutThrowing()
        {
            var controller = new StatisticsSurfaceController(new ThrowingOrderItemSearcher());

            var result = controller.GetAvailableValues(new KeyValueRequest { Keys = new List<string> { "Type" } }) as JsonResult;
            var response = result?.Value as KeyValueResult;

            Assert.IsNotNull(response);
            Assert.IsFalse(response.Success);
            StringAssert.Contains(response.Message, "Failed to fetch available values for keys:");
        }

        class StubOrderItemSearcher : IOrderItemSearcher
        {
            private readonly IEnumerable<OrderItemModel> _orders;

            public StubOrderItemSearcher(IEnumerable<OrderItemModel> orders)
            {
                _orders = orders;
            }

            public IEnumerable<OrderItemModel> Search(string query) => _orders;
            public SearchResult Search(string query, int start, int size) => new SearchResult { Count = 0, Items = _orders };
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => _orders;
            public IEnumerable<string> AggregatedProviders() => new List<string>();
            public void Added(OrderItemModel item) { }
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }

        class ThrowingOrderItemSearcher : IOrderItemSearcher
        {
            public IEnumerable<OrderItemModel> Search(string query) => throw new System.InvalidOperationException("Search backend unavailable.");
            public SearchResult Search(string query, int start, int size) => throw new System.InvalidOperationException("Search backend unavailable.");
            public IEnumerable<OrderItemModel> Search(string query, int size, string[] fields) => throw new System.InvalidOperationException("Search backend unavailable.");
            public IEnumerable<string> AggregatedProviders() => throw new System.InvalidOperationException("Search backend unavailable.");
            public void Added(OrderItemModel item) { }
            public void Modified(OrderItemModel item) { }
            public void Deleted(OrderItemModel item) { }
        }
    }
}
