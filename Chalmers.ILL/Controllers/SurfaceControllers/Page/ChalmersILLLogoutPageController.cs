using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Tasks;

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

        public async Task<IActionResult> Index()
        {
            var customModel = new ChalmersILLLogoutPageModel();
            _memberInfoManager.PopulateModelWithMemberData(Request, Response, customModel);

            if (User.Identity.IsAuthenticated)
            {
                _memberInfoManager.GetCurrentMemberId(Request, Response);
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                _memberInfoManager.ClearMemberCache(Response);
            }

            return View("~/Views/ChalmersILLLogoutPage.cshtml", customModel);
        }
    }
}
