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
        private readonly Func<List<MemberAccount>> _loadAccounts;

        public FileRoleProvider() : this(MemberFileStore.Load) { }

        public FileRoleProvider(Func<List<MemberAccount>> loadAccounts)
        {
            _loadAccounts = loadAccounts;
        }

        public bool IsUserInRole(string username, string roleName)
        {
            var account = FindAccount(username);
            return account?.Roles?.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

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
