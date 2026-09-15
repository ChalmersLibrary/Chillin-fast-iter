using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Chalmers.ILL.Members
{
    // Minimal membership service backed by MemberFileStore instead of a database. Used to
    // inherit System.Web.Security.MembershipProvider (no equivalent on modern .NET, see fas 3);
    // only the members actually used elsewhere in the app (ValidateUser, GetUser, ChangePassword)
    // are implemented, so the NotSupportedException stubs the base class required are gone too.
    public class FileMembershipProvider
    {
        private static readonly PasswordHasher<MemberAccount> _hasher =
            new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
            {
                // System.Web.Helpers.Crypto.HashPassword/VerifyHashedPassword (PBKDF2-HMAC-SHA1,
                // 1000 iterations, 128-bit salt, 256-bit subkey, 0x00 format marker) is
                // byte-for-byte the same format PasswordHasher<T> produces in IdentityV2
                // compatibility mode, so existing hashes in members.json keep working - no forced
                // password reset for this migration (unlike the Umbraco removal).
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
            }));

        private readonly Func<List<MemberAccount>> _loadAccounts;
        private readonly Action<List<MemberAccount>> _saveAccounts;

        public FileMembershipProvider() : this(MemberFileStore.Load, MemberFileStore.Save) { }

        public FileMembershipProvider(Func<List<MemberAccount>> loadAccounts, Action<List<MemberAccount>> saveAccounts)
        {
            _loadAccounts = loadAccounts;
            _saveAccounts = saveAccounts;
        }

        public bool ValidateUser(string username, string password)
        {
            var account = FindAccount(username);
            return account != null && !string.IsNullOrEmpty(account.PasswordHash) && Verify(account, password);
        }

        public MemberAccount GetUser(string username) => FindAccount(username);

        public bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            var accounts = _loadAccounts();
            var account = accounts.FirstOrDefault(a => IsMatch(a, username));
            if (account == null || string.IsNullOrEmpty(account.PasswordHash) || !Verify(account, oldPassword))
                return false;

            account.PasswordHash = _hasher.HashPassword(account, newPassword);
            _saveAccounts(accounts);
            return true;
        }

        private static bool Verify(MemberAccount account, string password) =>
            _hasher.VerifyHashedPassword(account, account.PasswordHash, password) != PasswordVerificationResult.Failed;

        private MemberAccount FindAccount(string username) => _loadAccounts().FirstOrDefault(a => IsMatch(a, username));

        private static bool IsMatch(MemberAccount account, string username) =>
            string.Equals(account.Login, username, StringComparison.OrdinalIgnoreCase);
    }
}
