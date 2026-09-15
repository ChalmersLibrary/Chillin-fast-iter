using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Chalmers.ILL.OrderItems;
using System;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    public class ChalmersILLDiskPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;
        IOrderItemSearcher _searcher;

        public ChalmersILLDiskPageController(IMemberInfoManager memberInfoManager, IOrderItemSearcher searcher)
        {
            _memberInfoManager = memberInfoManager;
            _searcher = searcher;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLDiskPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);

            if (!String.IsNullOrEmpty(Request.Query["query"]))
            {
                customModel.OrderItems = _searcher.Search("((type:Bok AND status:(Infodisk OR Utlånad OR Transport OR Krävd OR Förlorad OR Förlorad\\?)) OR (type:Artikel AND status:Transport)) AND " +
                    "\"" + Request.Query["query"].ToString().Trim() + "\"");
            }

            return View("~/Views/ChalmersILLDiskPage.cshtml", customModel);
        }
    }
}
