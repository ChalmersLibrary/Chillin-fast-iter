using Chalmers.ILL.Models;
using System;
using Microsoft.AspNetCore.Mvc;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Configuration;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class MaintenanceSurfaceController : Controller
    {
        private static readonly log4net.ILog _log = log4net.LogManager.GetLogger(typeof(MaintenanceSurfaceController));

        IOrderItemManager _orderItemManager;
        IMediaItemManager _mediaItemManager;
        IChillinConfiguration _config;
        IOrderItemSearcher _orderItemSearcher;

        public MaintenanceSurfaceController(IOrderItemManager orderItemManager, IMediaItemManager mediaItemManager, IChillinConfiguration config, IOrderItemSearcher orderItemSearcher)
        {
            _orderItemManager = orderItemManager;
            _mediaItemManager = mediaItemManager;
            _config = config;
            _orderItemSearcher = orderItemSearcher;
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

        /// <summary>
        /// Rebuilds the search index from the order files on disk - the recovery path if the
        /// index and the orders fall out of sync (see TODO-remove-dotnet-framework.md, fas 7's
        /// "återställningsväg om indexet tappas"), and the only way to populate a fresh
        /// environment's search index from seeded/migrated order files.
        /// </summary>
        /// <returns>Json</returns>
        [HttpPost]
        public ActionResult RebuildSearchIndex()
        {
            var json = new ResultResponse();

            try
            {
                var count = 0;
                foreach (var item in OrderFileReader.ReadAll(_config.DataPath, _log))
                {
                    _orderItemSearcher.Added(item);
                    count++;
                }

                json.Success = true;
                json.Message = "Reindexed " + count + " orders.";
            }
            catch (Exception e)
            {
                _log.Error("Failed to rebuild the search index.", e);
                json.Success = false;
                json.Message = "Failed to rebuild the search index.";
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
