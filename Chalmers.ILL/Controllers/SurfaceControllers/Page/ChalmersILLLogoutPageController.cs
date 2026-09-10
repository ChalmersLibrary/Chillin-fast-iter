using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using System.Web.Mvc;
using System.Web.Security;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    // A user whose auth cookie has already expired must still be able to reach this page to
    // clear the separate, unsigned ChalmersILL cookie (see MemberInfoManager) — without
    // [AllowAnonymous] the global AuthorizeAttribute would redirect them to login instead.
    [AllowAnonymous]
    public class ChalmersILLLogoutPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;

        public ChalmersILLLogoutPageController(IMemberInfoManager memberInfoManager)
        {
            _memberInfoManager = memberInfoManager;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLLogoutPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);

            if (User.Identity.IsAuthenticated)
            {
                _memberInfoManager.GetCurrentMemberId(Request, Response);
                FormsAuthentication.SignOut();
                _memberInfoManager.ClearMemberCache(Response);
            }

            return View("~/Views/ChalmersILLLogoutPage.cshtml", customModel);
        }
    }
}
