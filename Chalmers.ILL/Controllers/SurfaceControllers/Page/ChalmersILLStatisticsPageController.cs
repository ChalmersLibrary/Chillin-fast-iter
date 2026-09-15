using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    public class ChalmersILLStatisticsPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;

        public ChalmersILLStatisticsPageController(IMemberInfoManager memberInfoManager)
        {
            _memberInfoManager = memberInfoManager;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLStatisticsPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);
            return View("~/Views/ChalmersILLStatisticsPage.cshtml", customModel);
        }
    }
}
