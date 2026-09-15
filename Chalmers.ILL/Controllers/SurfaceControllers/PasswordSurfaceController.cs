using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Chalmers.ILL.Members;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class PasswordSurfaceController : Controller
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(PasswordSurfaceController));

        // The form posts here directly (not to the settings page itself), so redirects must name
        // the settings page explicitly instead of reusing Request.Path.
        const string SettingsPageUrl = "/bestaellningar/instaellningar/";

        IMemberInfoManager _memberInfoManager;
        readonly Func<string, string, bool> _validateUser;
        readonly Func<string, string, string, bool> _changePassword;

        // See the same attribute on LoginSurfaceController for why this is required: without it,
        // ASP.NET Core's DI-based controller activation is ambiguous between this constructor and
        // the test-only one below, and throws on the very first request - invisible to unit tests.
        [ActivatorUtilitiesConstructor]
        public PasswordSurfaceController(IMemberInfoManager memberInfoManager, FileMembershipProvider membershipProvider)
            : this(memberInfoManager, membershipProvider.ValidateUser, membershipProvider.ChangePassword)
        {
        }

        // Allows tests to control the membership outcome without a configured Membership provider.
        public PasswordSurfaceController(IMemberInfoManager memberInfoManager, Func<string, string, bool> validateUser, Func<string, string, string, bool> changePassword)
        {
            _memberInfoManager = memberInfoManager;
            _validateUser = validateUser;
            _changePassword = changePassword;
        }

        [HttpGet]
        public ActionResult RenderChangePasswordAction()
        {
            return PartialView("Settings/ChangePassword", new Models.PartialPage.Settings.ChangePassword());
        }

        [HttpPost]
        public ActionResult ChangePassword(Models.PartialPage.Settings.ChangePassword model)
        {
            if (!ModelState.IsValid)
                return Redirect(SettingsPageUrl + "?error=invalid-model");

            var loginName = _memberInfoManager.GetCurrentMemberLoginName(Request, Response);

            // Validate the current password via the configured membership provider
            if (!_validateUser(loginName, model.CurrentPassword))
                return Redirect(SettingsPageUrl + "?error=invalid-member");

            try
            {
                if (_changePassword(loginName, model.CurrentPassword, model.NewPassword))
                    return Redirect(SettingsPageUrl + "?success=true");

                _log.Warn($"ChangePassword returned false for user '{loginName}'.");
                return Redirect(SettingsPageUrl + "?error=invalid-member");
            }
            catch (Exception ex)
            {
                _log.Error($"ChangePassword threw for user '{loginName}'.", ex);
                return Redirect(SettingsPageUrl + "?error=invalid-member");
            }
        }
    }
}
