using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    // Account management for the file-based member store (see MemberFileStore). Equivalent to
    // the access Umbraco's old backoffice admin interface gave superusers, so it's gated behind
    // a separate SuperAdmin role rather than the general Administrator role used elsewhere.
    [Authorize(Roles = "SuperAdmin")]
    public class MemberAdminSurfaceController : Controller
    {
        IMemberAdminService _memberAdminService;

        public MemberAdminSurfaceController(IMemberAdminService memberAdminService)
        {
            _memberAdminService = memberAdminService;
        }

        [HttpGet]
        public ActionResult RenderMemberAdminAction()
        {
            var pageModel = new Models.PartialPage.Settings.MemberAdmin
            {
                Members = _memberAdminService.GetAllMembers()
                    .Select(a => new Models.PartialPage.Settings.MemberSummary { Login = a.Login, Roles = a.Roles })
                    .OrderBy(a => a.Login)
                    .ToList()
            };

            return PartialView("Settings/MemberAdmin", pageModel);
        }

        [HttpPost]
        public ActionResult CreateMember(string login, string password, string roles)
        {
            var json = new ResultResponse();

            try
            {
                _memberAdminService.CreateMember(login, password, ParseRoles(roles));
                json.Success = true;
                json.Message = "Kontot skapades.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Fel: " + e.Message;
            }

            return Json(json);
        }

        [HttpPost]
        public ActionResult SetMemberPassword(string login, string newPassword)
        {
            var json = new ResultResponse();

            try
            {
                _memberAdminService.SetPassword(login, newPassword);
                json.Success = true;
                json.Message = "Lösenordet uppdaterades.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Fel: " + e.Message;
            }

            return Json(json);
        }

        [HttpPost]
        public ActionResult SetMemberRoles(string login, string roles)
        {
            var json = new ResultResponse();

            try
            {
                _memberAdminService.SetRoles(login, ParseRoles(roles));
                json.Success = true;
                json.Message = "Roller uppdaterades.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Fel: " + e.Message;
            }

            return Json(json);
        }

        [HttpPost]
        public ActionResult DeleteMember(string login)
        {
            var json = new ResultResponse();

            try
            {
                _memberAdminService.DeleteMember(login);
                json.Success = true;
                json.Message = "Kontot togs bort.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Fel: " + e.Message;
            }

            return Json(json);
        }

        private static System.Collections.Generic.List<string> ParseRoles(string roles) =>
            (roles ?? "").Split(',').Select(r => r.Trim()).Where(r => r != "").ToList();
    }
}
