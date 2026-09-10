using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using Chalmers.ILL.Members;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class PasswordSurfaceController : Controller
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(PasswordSurfaceController));

        // The form posts here directly (not to the settings page itself), so redirects must name
        // the settings page explicitly instead of reusing Request.Url.AbsolutePath.
        const string SettingsPageUrl = "/bestaellningar/instaellningar/";

        IMemberInfoManager _memberInfoManager;
        readonly Func<string, string, bool> _validateUser;
        readonly Func<string, string, string, bool> _changePassword;

        public PasswordSurfaceController(IMemberInfoManager memberInfoManager)
            : this(memberInfoManager, Membership.ValidateUser, ChangePasswordViaMembership)
        {
        }

        // Allows tests to control the membership outcome without a configured Membership provider.
        public PasswordSurfaceController(IMemberInfoManager memberInfoManager, Func<string, string, bool> validateUser, Func<string, string, string, bool> changePassword)
        {
            _memberInfoManager = memberInfoManager;
            _validateUser = validateUser;
            _changePassword = changePassword;
        }

        private static bool ChangePasswordViaMembership(string loginName, string oldPassword, string newPassword) =>
            Membership.GetUser(loginName).ChangePassword(oldPassword, newPassword);

        [HttpGet]
        public ActionResult RenderChangePasswordAction()
        {
            return PartialView("Settings/ChangePassword", new Models.PartialPage.Settings.ChangePassword());
        }

        [HttpPost]
        public ActionResult ChangePassword(Models.PartialPage.Settings.ChangePassword model)
        {
            if (ModelState.IsValid)
            {
                var loginName = _memberInfoManager.GetCurrentMemberLoginName(Request, Response);

                // Validate the current password via the configured membership provider
                if (_validateUser(loginName, model.CurrentPassword))
                {
                    try
                    {
                        if (_changePassword(loginName, model.CurrentPassword, model.NewPassword))
                        {
                            Response.Redirect(SettingsPageUrl + "?success=true");
                        }
                        else
                        {
                            _log.Warn($"ChangePassword returned false for user '{loginName}'.");
                            Response.Redirect(SettingsPageUrl + "?error=invalid-member");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"ChangePassword threw for user '{loginName}'.", ex);
                        Response.Redirect(SettingsPageUrl + "?error=invalid-member");
                    }
                }
                else
                {
                    Response.Redirect(SettingsPageUrl + "?error=invalid-member");
                }
            }
            else
            {
                Response.Redirect(SettingsPageUrl + "?error=invalid-model");
            }

            return Redirect(SettingsPageUrl);
        }
    }
}