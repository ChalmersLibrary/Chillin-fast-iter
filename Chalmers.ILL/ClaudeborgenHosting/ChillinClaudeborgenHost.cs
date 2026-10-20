using System.Linq;
using System.Security.Claims;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models.Page;
using Claudeborgen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Chalmers.ILL.ClaudeborgenHosting
{
    // Chillin's side of the Claudeborgen boundary (claudeborgen/DESIGN.md, "Gränsen mot
    // värdappen"): Chillin's roles -> Claudeborgen access, and Chillin's layout around a
    // conversation page. Everything else is in the Claudeborgen.Web package.
    public class ChillinClaudeborgenHost : IClaudeborgenHost
    {
        private readonly IMemberInfoManager _memberInfoManager;

        public ChillinClaudeborgenHost(IMemberInfoManager memberInfoManager)
        {
            _memberInfoManager = memberInfoManager;
        }

        // Reading needs only what every Chillin page needs (logged in, at least one role -
        // ViewerReadOnlyFilter blocks role-less accounts before this is ever asked), so every
        // account that can see Chillin can read everything here.
        public ClaudeAccess AccessFor(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return ClaudeAccess.None;

            var isOwner = user.IsInRole(FileRoleProvider.ObjektSpecialistRole);
            var isIt = user.IsInRole(FileRoleProvider.ObjektSpecialistITRole);

            return new ClaudeAccess
            {
                CanRead = user.Claims.Any(c => c.Type == ClaimTypes.Role),
                CanWrite = isOwner || isIt,
                CanStartAndClose = isOwner,
                IsIt = isIt,
            };
        }

        public IActionResult ConversationPage(Controller controller, ClaudePageModel model)
        {
            var page = new ChillinClaudePageModel { Claude = model };
            _memberInfoManager.PopulateModelWithMemberData(controller.Request, controller.Response, page);
            return controller.View("~/ClaudeborgenHosting/Conversation.cshtml", page);
        }
    }

    public class ChillinClaudePageModel : ChalmersILLModel
    {
        public ClaudePageModel Claude { get; set; }
    }

    // Lets the read-only accounts (Viewer and the Claudeborgen roles, see ViewerReadOnlyFilter)
    // past that filter on Claudeborgen's own controllers - same as if they carried [AllowViewer]
    // themselves, which a package can't. The filter is about Chillin's own data; every write in
    // Claudeborgen checks ChillinClaudeborgenHost.AccessFor itself. Added to the selectors'
    // endpoint metadata, which is exactly where ViewerReadOnlyFilter looks.
    public class ClaudeborgenAllowViewerConvention : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            if (controller.ControllerType.Assembly != typeof(ClaudeController).Assembly)
                return;

            foreach (var selector in controller.Actions.SelectMany(a => a.Selectors))
                selector.EndpointMetadata.Add(new AllowViewerAttribute());
        }
    }
}
