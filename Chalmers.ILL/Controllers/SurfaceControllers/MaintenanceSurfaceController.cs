using Chalmers.ILL.Models;
using System;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.MediaItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class MaintenanceSurfaceController : Controller
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MaintenanceSurfaceController));

        IOrderItemManager _orderItemManager;
        IMediaItemManager _mediaItemManager;

        public MaintenanceSurfaceController(IOrderItemManager orderItemManager, IMediaItemManager mediaItemManager)
        {
            _orderItemManager = orderItemManager;
            _mediaItemManager = mediaItemManager;
        }

        /// <summary>
        /// Method which will run different maintenance jobs.
        /// </summary>
        /// <returns>Json</returns>
        [HttpPost]
        public ActionResult RunMaintenanceJobs()
        {
            var json = new ResultResponse();

            json.Success = true;

            removeOldMediaItems(json);

            if (json.Success)
            {
                json.Message = "All maintenance jobs ran successfully.";
            }

            return Json(json);
        }

        private void removeOldMediaItems(ResultResponse res)
        {
            try
            {
                var deletedMediaItemIds = _mediaItemManager.DeleteOlderThan(DateTime.Now.AddDays(-30));

                foreach (var idCollection in deletedMediaItemIds)
                {
                    _orderItemManager.RemoveConnectionToMediaItem(idCollection.OrderItemId, idCollection.MediaItemId);
                }
            }
            catch (Exception e)
            {
                _log.Error("Failed to remove old media items.", e);
                res.Success = false;
                res.Message += "Failed to remove old media items. ";
            }
        }
    }
}
