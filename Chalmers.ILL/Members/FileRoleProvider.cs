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
        // "Viewer" is not special to this class - it's just an ordinary role string an admin can
        // assign like any other (see MemberAdminSurfaceController.ParseRoles). It's read-only by
        // convention, enforced by ViewerReadOnlyFilter, which also separately blocks an account
        // with NO roles at all from everything - see that file. This constant exists purely so
        // ViewerReadOnlyFilter and MemberAdminSurfaceController don't each hardcode the string.
        public const string ViewerRole = "Viewer";

        // The only role with write access (orders, settings, members' own data) - every other role
        // is read-only, see ViewerReadOnlyFilter.
        public const string AdministratorRole = "Administrator";

        // Claudeborgen (see claudeborgen-DESIGN.md): the object owner and the IT specialist. Like
        // every non-Administrator role they're read-only for Chillin's own data; they only grant
        // access to the Claude integration.
        public const string ObjektSpecialistRole = "ObjektSpecialist";
        public const string ObjektSpecialistITRole = "ObjektSpecialistIT";

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
            return account?.Roles?.ToArray() ?? new string[0];
        }

        public string[] GetAllRoles() =>
            _loadAccounts().SelectMany(a => a.Roles ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public bool RoleExists(string roleName) =>
            GetAllRoles().Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));

        private MemberAccount FindAccount(string username) =>
            _loadAccounts().FirstOrDefault(a => string.Equals(a.Login, username, StringComparison.OrdinalIgnoreCase));
    }
}
