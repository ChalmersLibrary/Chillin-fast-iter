using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers.Page
{
    [AllowAnonymous]
    public class ChalmersILLLoginPageController : Controller
    {
        public ActionResult Index()
        {
            return View("~/Views/ChalmersILLLoginPage.cshtml");
        }
    }
}
