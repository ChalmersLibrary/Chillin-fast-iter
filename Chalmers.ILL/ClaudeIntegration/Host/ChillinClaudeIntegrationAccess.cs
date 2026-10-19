using System.Linq;
using System.Security.Claims;
using Chalmers.ILL.Members;

namespace Chalmers.ILL.ClaudeIntegration.Host
{
    // Chillin's roles -> the integration's access levels. Reading needs only what every Chillin page
    // needs (logged in, at least one role - ViewerReadOnlyFilter blocks role-less accounts before
    // this is ever asked), so every account that can see Chillin can read everything here.
    public class ChillinClaudeIntegrationAccess : IClaudeIntegrationAccess
    {
        public ClaudeAccess For(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return ClaudeAccess.None;

            var hasAnyRole = user.Claims.Any(c => c.Type == ClaimTypes.Role);
            var isOwner = user.IsInRole(FileRoleProvider.ObjektSpecialistRole);
            var isIt = user.IsInRole(FileRoleProvider.ObjektSpecialistITRole);

            return new ClaudeAccess
            {
                CanRead = hasAnyRole,
                CanWrite = isOwner || isIt,
                CanStartAndClose = isOwner,
                IsIt = isIt,
            };
        }
    }
}
