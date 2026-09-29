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

    // Enforces two tiers below "has a real role" (Desk/Administrator/SuperAdmin/...). Registered
    // as a global MVC filter (see FilterConfig.RegisterGlobalFilters) alongside the login-required
    // AuthorizeFilter, which already runs first and handles the "not logged in at all" /
    // "AllowAnonymous" cases - by the time this filter runs, a request has either already been
    // redirected/rejected by that filter, or the user is authenticated (or the action is
    // [AllowAnonymous], which this filter also leaves alone since an anonymous caller has no role
    // claims to restrict).
    //
    // Tier 1, "Viewer": every role claim the user carries is "Viewer" - read-only, may reach
    // actions marked [AllowViewer]. Explicitly assigned (MemberAdminSurfaceController), never a
    // default - see FileRoleProvider.
    //
    // Tier 2, no roles at all: blocked from everything, [AllowViewer] included - an account with
    // literally zero roles assigned gets no access, per Lars 2026-09-29 ("saknar man roles så får
    // man inte se något"). A user who holds "Viewer" alongside a real role (e.g. "Desk") is
    // unaffected by either tier, same as before this filter existed.
    public class ViewerReadOnlyFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context.Filters.Any(f => f is IAllowAnonymousFilter)) return;

            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true) return;

            var roles = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            if (roles.Count == 0)
            {
                context.Result = new ForbidResult();
                return;
            }

            var isViewerOnly = roles.All(r => string.Equals(r, FileRoleProvider.ViewerRole, StringComparison.OrdinalIgnoreCase));
            if (!isViewerOnly) return;

            if (context.ActionDescriptor.EndpointMetadata.Any(m => m is AllowViewerAttribute)) return;

            context.Result = new ForbidResult();
        }
    }
}
