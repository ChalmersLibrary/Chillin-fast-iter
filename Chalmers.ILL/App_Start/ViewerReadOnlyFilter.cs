using System;
using System.Linq;
using System.Security.Claims;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Chalmers.ILL
{
    // Marks an action (or a whole controller) as safe for the read-only "Viewer" role (see
    // FileRoleProvider.ViewerRole) - i.e. it only reads data, it never creates/edits/deletes an
    // order, member, template or chillin-text. Deliberately opt-in (default-deny): a new action
    // added later is blocked for Viewer unless someone consciously marks it readable, rather than
    // silently writable because nobody remembered to add it to a block-list.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AllowViewerAttribute : Attribute
    {
    }

    // Enforces the "Viewer" role as read-only. Registered as a global MVC filter (see
    // FilterConfig.RegisterGlobalFilters) alongside the login-required AuthorizeFilter, which
    // already runs first and handles the "not logged in at all" / "AllowAnonymous" cases - by the
    // time this filter runs, a request has either already been redirected/rejected by that filter,
    // or the user is authenticated (or the action is [AllowAnonymous], which this filter also
    // leaves alone since an anonymous caller has no role claims to restrict).
    //
    // A user is "Viewer-restricted" when every role claim they carry is "Viewer" (this also covers
    // a user with zero role claims at all, matching FileRoleProvider's "no roles assigned = Viewer"
    // default) - i.e. they have no other, more privileged role. A user who happens to hold "Viewer"
    // alongside a real role (e.g. "Desk") is unaffected, same as today.
    public class ViewerReadOnlyFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context.Filters.Any(f => f is IAllowAnonymousFilter)) return;

            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true) return;

            var roles = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value);
            var isRestrictedToViewer = !roles.Any(r => !string.Equals(r, FileRoleProvider.ViewerRole, StringComparison.OrdinalIgnoreCase));
            if (!isRestrictedToViewer) return;

            if (context.ActionDescriptor.EndpointMetadata.Any(m => m is AllowViewerAttribute)) return;

            context.Result = new ForbidResult();
        }
    }
}
