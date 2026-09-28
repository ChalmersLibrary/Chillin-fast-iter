using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

if (args.Length != 1 || string.IsNullOrEmpty(args[0]))
{
    Console.Error.WriteLine("Usage: dotnet run --project Chalmers.ILL.HashPassword -- <password>");
    Console.Error.WriteLine("Prints a PasswordHash value ready to paste into members.json.");
    return 1;
}

// Same PasswordHasherOptions as FileMembershipProvider/MemberAdminService - must match exactly,
// or a hash generated here won't verify when the app reads it back.
var hasher = new PasswordHasher<MemberAccount>(Options.Create(new PasswordHasherOptions
{
    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
}));

Console.WriteLine(hasher.HashPassword(new MemberAccount(), args[0]));
return 0;
