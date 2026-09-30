using System;
using System.Linq;
using Chalmers.ILL.Members;
using Chalmers.ILL.Models;
using Chalmers.ILL.OrderItems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace Chalmers.ILL
{
    // Marks a mutating OrderItem action as requiring the caller to hold the order's edit lock
    // (OrderItemModel.EditedBy, set/cleared via OrderItemSurfaceController's
    // LockOrderItem/UnlockOrderItem/TakeOverLockedOrderItem). Opt-in, same trade-off
    // ViewerReadOnlyFilter/AllowViewerAttribute already accepts for role-gating: a new mutating
    // action added later stays unprotected until someone consciously marks it, rather than the
    // filter guessing from the HTTP verb or method name (which ViewerReadOnlyFilterTest's
    // GetVerbMutations_NeverCarryAllowViewer already shows is unreliable here - several
    // mutations are exposed over [HttpGet]).
    //
    // Found 2026-09-30: no controller besides OrderItemSurfaceController itself ever checked
    // EditedBy before this - the lock only ever gated which HTML got rendered
    // (Chalmers.ILL.OrderItem.cshtml's top-level if), never whether a mutation was actually
    // allowed to go through. Confirmed live: a second member could call
    // OrderItemStatusSurfaceController.SetOrderItemStatus (and everything else that mutates
    // order state) on an order a different member had locked, with no error and no lock check
    // performed at all. Read-only Render*/Query*-style actions are deliberately left unmarked -
    // the locked branch of the partial doesn't even link to them for a non-holder, and they
    // don't mutate anything.
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RequiresOrderLockAttribute : Attribute
    {
        // The int (or int[]) action-argument to read the order's NodeId(s) from. Exactly one of
        // Parameter/PackJsonField must be set.
        public string Parameter { get; set; }

        // Some mutations take a single JSON "packJson" string argument instead of a bound nodeId
        // parameter (e.g. OrderItemClaimSurfaceController.ClaimItemPackage) - the field name
        // inside that JSON to read the NodeId from. Casing/name varies by package class
        // ("nodeId" in most, "orderNodeId" in OrderItemReceiveBookSurfaceController's
        // DeliveryReceivedPackage), so this must match the specific package's field.
        public string PackJsonField { get; set; }
    }

    // Enforces RequiresOrderLockAttribute globally so each action needs only the one-line
    // attribute instead of a repeated lock check plus adding IMemberInfoManager/IOrderItemManager
    // as a constructor dependency (and updating every existing test's DI wiring) across the
    // ~15 controllers that mutate order state. Registered in FilterConfig.RegisterGlobalFilters.
    //
    // Runs as IActionFilter (after model binding, so ActionArguments holds the real bound
    // values), not IAuthorizationFilter like ViewerReadOnlyFilter - that runs too early to see
    // an action's bound parameters.
    public class RequiresOrderLockFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var attribute = context.ActionDescriptor.EndpointMetadata.OfType<RequiresOrderLockAttribute>().FirstOrDefault();
            if (attribute == null) return;

            var nodeIds = ResolveNodeIds(context, attribute);
            if (nodeIds == null || nodeIds.Length == 0) return;

            var orderItemManager = (IOrderItemManager)context.HttpContext.RequestServices.GetService(typeof(IOrderItemManager));
            var memberInfoManager = (IMemberInfoManager)context.HttpContext.RequestServices.GetService(typeof(IMemberInfoManager));
            var currentMemberId = memberInfoManager.GetCurrentMemberId(context.HttpContext.Request, context.HttpContext.Response).ToString();

            foreach (var nodeId in nodeIds)
            {
                var orderItem = orderItemManager.GetOrderItem(nodeId);
                if (orderItem == null) continue;

                if (orderItem.EditedBy != "" && orderItem.EditedBy != currentMemberId)
                {
                    context.Result = new JsonResult(new ResultResponse(false,
                        "Ordern är låst av " + orderItem.EditedByMemberName + " och kan inte ändras just nu."));
                    return;
                }
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // NOP
        }

        // A caller who's genuinely trying to bypass the lock can't get a free pass just because
        // it can't be resolved this way (Parameter/PackJsonField are always set explicitly by the
        // action it's applied to) - but this deliberately doesn't throw, since the actors here are
        // authenticated staff, not an adversary: the point of this filter is to stop accidental
        // concurrent edits, not to be a security boundary, so a filter bug should never be able to
        // take an unrelated action down with it.
        private static int[] ResolveNodeIds(ActionExecutingContext context, RequiresOrderLockAttribute attribute)
        {
            if (attribute.Parameter != null)
            {
                if (!context.ActionArguments.TryGetValue(attribute.Parameter, out var value) || value == null) return null;
                if (value is int single) return new[] { single };
                if (value is int[] many) return many;

                // A handful of actions bind a whole model (e.g. OrderItemMailSurfaceController.
                // SendMail's OutgoingMailPackageModel) instead of a plain nodeId parameter -
                // read its "nodeId"/"NodeId" field or property via reflection rather than adding
                // a third attribute mode for what's really the same "where's the id" question.
                var type = value.GetType();
                var field = type.GetField("nodeId") ?? type.GetField("NodeId");
                if (field != null) return new[] { (int)field.GetValue(value) };
                var property = type.GetProperty("nodeId") ?? type.GetProperty("NodeId");
                if (property != null) return new[] { (int)property.GetValue(value) };

                return null;
            }

            if (attribute.PackJsonField != null && context.ActionArguments.TryGetValue("packJson", out var packJsonValue) && packJsonValue is string packJson)
            {
                try
                {
                    var obj = JObject.Parse(packJson);
                    var token = obj[attribute.PackJsonField];
                    return token != null ? new[] { token.ToObject<int>() } : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
