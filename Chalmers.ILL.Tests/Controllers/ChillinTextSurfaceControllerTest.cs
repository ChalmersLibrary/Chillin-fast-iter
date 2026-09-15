using System;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.Controllers.SurfaceControllers;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.PartialPage.Settings;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Controllers
{
    [TestClass]
    public class ChillinTextSurfaceControllerTest
    {
        [TestMethod]
        public void RenderChillinTextsAction_ReturnsPartialViewWithAllTexts()
        {
            var dto = new ChillinTextDto { Id = "default" };
            var controller = MakeController(allResult: dto);

            var result = controller.RenderChillinTextsAction() as PartialViewResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Settings/ChillinText", result.ViewName);
            Assert.AreSame(dto, result.Model);
        }

        [TestMethod]
        public void Save_WithValidData_ReturnsTrueJson()
        {
            var chillinText = new ChillinText { CheckInNote = "in", CheckOutNote = "out", StandardTitleText = "std" };
            var controller = MakeController(deserializeResult: chillinText);

            var result = controller.Save("someId", "{}") as JsonResult;
            var json = (ResultResponse)result.Value;

            Assert.IsTrue(json.Success);
        }

        [TestMethod]
        public void Save_WhenRepositoryThrows_ReturnsFalseJsonWithErrorMessage()
        {
            var controller = MakeController(
                deserializeResult: new ChillinText(),
                throwOnPut: new Exception("db error"));

            var result = controller.Save("id", "{}") as JsonResult;
            var json = (ResultResponse)result.Value;

            Assert.IsFalse(json.Success);
            Assert.IsTrue(json.Message.Contains("db error"));
        }

        private static ChillinTextSurfaceController MakeController(
            ChillinTextDto allResult = null,
            ChillinText deserializeResult = null,
            Exception throwOnPut = null)
        {
            return new ChillinTextSurfaceController(
                new StubRepo(allResult, throwOnPut),
                new StubJson(deserializeResult));
        }

        class StubRepo : IChillinTextRepository
        {
            private readonly ChillinTextDto _all;
            private readonly Exception _throwOnPut;
            public StubRepo(ChillinTextDto all, Exception throwOnPut) { _all = all; _throwOnPut = throwOnPut; }
            public ChillinTextDto All() => _all;
            public void Put(string id, ChillinText t) { if (_throwOnPut != null) throw _throwOnPut; }
            public ChillinText ByTextField(string f) => null;
        }

        class StubJson : IJsonService
        {
            private readonly object _result;
            public StubJson(object result) { _result = result; }
            public T DeserializeObject<T>(string obj) => (T)_result;
            public string SerializeObject<T>(T obj) => "";
        }
    }
}
