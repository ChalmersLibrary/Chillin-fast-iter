using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    public class ChalmersILLSettingsPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;

        public ChalmersILLSettingsPageController(IMemberInfoManager memberInfoManager)
        {
            _memberInfoManager = memberInfoManager;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLSettingsPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);
            return View("~/Views/ChalmersILLSettingsPage.cshtml", customModel);
        }
    }
}
