using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.Members
{
    // Minimal role service backed by MemberFileStore. Used to inherit
    // System.Web.Security.RoleProvider (no equivalent on modern .NET, see fas 3). Roles are
    // assigned by editing the JSON file directly, so only the read-side members used elsewhere
    // in the app are implemented.
    public class FileRoleProvider
    {
        // An account with no roles assigned is implicitly "Viewer" - a read-only default rather
        // than "no access at all", since the global AuthorizeFilter already lets any logged-in
        // account reach every page/action regardless of roles (see ViewerReadOnlyFilter, which
        // is what actually enforces the read-only restriction this role implies). An *unknown*
        // account (FindAccount returns null) is NOT given this default - that's "not logged in",
        // not "logged in with no roles".
        public const string ViewerRole = "Viewer";

        private readonly Func<List<MemberAccount>> _loadAccounts;

        public FileRoleProvider(Func<List<MemberAccount>> loadAccounts)
        {
            _loadAccounts = loadAccounts;
        }

        public bool IsUserInRole(string username, string roleName) =>
            GetRolesForUser(username).Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));

        public string[] GetRolesForUser(string username)
        {
            var account = FindAccount(username);
            if (account == null) return new string[0];
            return EffectiveRoles(account.Roles);
        }

        public string[] GetAllRoles() =>
            _loadAccounts().SelectMany(a => EffectiveRoles(a.Roles)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private static string[] EffectiveRoles(List<string> roles) =>
            (roles == null || roles.Count == 0) ? new[] { ViewerRole } : roles.ToArray();

        public bool RoleExists(string roleName) =>
            GetAllRoles().Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));

        private MemberAccount FindAccount(string username) =>
            _loadAccounts().FirstOrDefault(a => string.Equals(a.Login, username, StringComparison.OrdinalIgnoreCase));
    }
}
