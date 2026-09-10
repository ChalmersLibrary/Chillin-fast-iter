using System;
using System.Collections.Specialized;
using System.Linq;
using System.Web.Security;

namespace Chalmers.ILL.Members
{
    // Minimal RoleProvider backed by MemberFileStore. Roles are assigned by editing the JSON
    // file directly, so only the read-side members used elsewhere in the app are implemented.
    public class FileRoleProvider : RoleProvider
    {
        private readonly Func<System.Collections.Generic.List<MemberAccount>> _loadAccounts;

        public FileRoleProvider() : this(MemberFileStore.Load) { }

        public FileRoleProvider(Func<System.Collections.Generic.List<MemberAccount>> loadAccounts)
        {
            _loadAccounts = loadAccounts;
        }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(string.IsNullOrEmpty(name) ? "FileRoleProvider" : name, config ?? new NameValueCollection());
        }

        public override string ApplicationName { get; set; } = "Chillin";

        public override bool IsUserInRole(string username, string roleName)
        {
            var account = FindAccount(username);
            return account?.Roles?.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        public override string[] GetRolesForUser(string username)
        {
            var account = FindAccount(username);
            return account?.Roles?.ToArray() ?? new string[0];
        }

        public override string[] GetAllRoles() =>
            _loadAccounts().SelectMany(a => a.Roles ?? new System.Collections.Generic.List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public override bool RoleExists(string roleName) =>
            GetAllRoles().Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));

        private MemberAccount FindAccount(string username) =>
            _loadAccounts().FirstOrDefault(a => string.Equals(a.Login, username, StringComparison.OrdinalIgnoreCase));

        public override void AddUsersToRoles(string[] usernames, string[] roleNames) => throw new NotSupportedException();

        public override void CreateRole(string roleName) => throw new NotSupportedException();

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) => throw new NotSupportedException();

        public override string[] FindUsersInRole(string roleName, string usernameToMatch) => throw new NotSupportedException();

        public override string[] GetUsersInRole(string roleName) => throw new NotSupportedException();

        public override void RemoveUsersFromRoles(string[] usernames, string[] roleNames) => throw new NotSupportedException();
    }
}
