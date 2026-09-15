using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    public class ChalmersILLStartPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;

        public ChalmersILLStartPageController(IMemberInfoManager memberInfoManager)
        {
            _memberInfoManager = memberInfoManager;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLStartPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);
            return View("~/Views/ChalmersILLStartPage.cshtml", customModel);
        }
    }
}
