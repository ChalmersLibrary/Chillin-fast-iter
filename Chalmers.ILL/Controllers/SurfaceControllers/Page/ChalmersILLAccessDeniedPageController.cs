using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    // Where cookie authentication's Forbid() (see ViewerReadOnlyFilter and
    // MemberAdminSurfaceController's [Authorize(Roles = "SuperAdmin")]) sends a logged-in user who
    // is blocked from the page/action they tried - wired up via AccessDeniedPath in Program.cs.
    // [AllowAnonymous] is required, not just convenient: a roleless account is blocked from
    // *everything* by ViewerReadOnlyFilter, so without this it would be denied access to the very
    // page explaining that it was denied access, looping back through Forbid() forever.
    [AllowAnonymous]
    public class ChalmersILLAccessDeniedPageController : Controller
    {
        IMemberInfoManager _memberInfoManager;

        public ChalmersILLAccessDeniedPageController(IMemberInfoManager memberInfoManager)
        {
            _memberInfoManager = memberInfoManager;
        }

        public ActionResult Index()
        {
            var customModel = new ChalmersILLAccessDeniedPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);
            return View("~/Views/ChalmersILLAccessDeniedPage.cshtml", customModel);
        }
    }
}
