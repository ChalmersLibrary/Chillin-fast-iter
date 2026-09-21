using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Chalmers.ILL.Members;
using Chalmers.ILL.Configuration;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [AllowAnonymous]
    public class LoginSurfaceController : Controller
    {
        IMemberInfoManager _memberInfoManager;
        readonly Func<string, string, bool> _validateUser;
        readonly Func<string, IEnumerable<string>> _getRolesForUser;
        readonly Func<HttpContext, string, IEnumerable<string>, Task> _signIn;
        readonly IChillinConfiguration _config;

        // Without this attribute, ASP.NET Core's ActivatorUtilities can't tell this constructor
        // apart from the test-only one below (both have parameter types it could, in principle,
        // resolve or default to null) and throws "Multiple constructors accepting all given
        // argument types" at the first real request - invisible to unit tests, which construct
        // the controller directly and never go through DI at all.
        [ActivatorUtilitiesConstructor]
        public LoginSurfaceController(IMemberInfoManager memberInfoManager, FileMembershipProvider membershipProvider, FileRoleProvider roleProvider, IChillinConfiguration config)
            : this(memberInfoManager, membershipProvider.ValidateUser, roleProvider.GetRolesForUser, SignInWithCookie, config)
        {
        }

        // Allows tests to control the membership/role outcome and avoid a real cookie sign-in,
        // without a configured Membership/Role provider.
        public LoginSurfaceController(IMemberInfoManager memberInfoManager, Func<string, string, bool> validateUser, Func<string, IEnumerable<string>> getRolesForUser, Func<HttpContext, string, IEnumerable<string>, Task> signIn, IChillinConfiguration config)
        {
            _memberInfoManager = memberInfoManager;
            _validateUser = validateUser;
            _getRolesForUser = getRolesForUser;
            _signIn = signIn;
            _config = config;
        }

        // Was FormsAuthentication.SetAuthCookie(model.Login, false) plus a separate, live
        // Roles.IsUserInRole lookup on every subsequent request. ASP.NET Core's [Authorize(Roles=..)]
        // checks claims baked into the auth cookie at sign-in time instead, so role edits now take
        // effect on next login rather than immediately - a real behavior change, not just a rewrite.
        private static Task SignInWithCookie(HttpContext httpContext, string login, IEnumerable<string> roles)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, login) };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        // Must match Program.cs's CookieAuthenticationOptions.LoginPath - this is the GET-able
        // page that renders Chalmers.ILL.Login.cshtml (which reads ?error=... back out), not the
        // POST-only route this action itself is reached through. Redirecting to Request.Path.Value
        // instead (the bug this replaced) sent the browser's follow-up GET back at this same
        // [HttpPost]-only action, which 405'd - a real network error page instead of the intended
        // "fel login/lösenord" message, on every failed login attempt. Never caught by
        // LoginSurfaceControllerTest because that test hardcodes HttpContext.Request.Path to
        // "/login" rather than the real posted-to URL, so it mirrored the bug instead of catching it.
        private const string LoginPageUrl = "/ChalmersILLLoginPage";

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HandleLogin(Models.LoginModel model)
        {
            if (!ModelState.IsValid)
                return Redirect(LoginPageUrl + "?error=invalid-model");

            if (!_validateUser(model.Login, model.Password))
                return Redirect(LoginPageUrl + "?error=invalid-member");

            var roles = _getRolesForUser(model.Login).ToList();
            await _signIn(HttpContext, model.Login, roles);
            _memberInfoManager.AddMemberToCache(Response, 0, model.Login, model.Login);

            var redirectUrl = roles.Any(r => string.Equals(r, "Desk", StringComparison.OrdinalIgnoreCase))
                ? "/disk/?login=ok"
                : _config.OrderListPageUrl + "?login=ok";
            return Redirect(redirectUrl);
        }
    }
}
