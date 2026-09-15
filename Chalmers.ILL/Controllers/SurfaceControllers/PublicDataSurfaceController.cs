using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Extensions;
using System.Globalization;
using Chalmers.ILL.OrderItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    // Documented public API (see ILL-status-api.md) called cross-origin by the library system
    // with no user login, so it must be exempt from the global AuthorizeAttribute.
    [AllowAnonymous]
    public class PublicDataSurfaceController : Controller
    {
        IBulkDataManager _bulkDataManager;

        public PublicDataSurfaceController(IBulkDataManager bulkDataManager)
        {
            _bulkDataManager = bulkDataManager;
        }

        [HttpGet]
        public ActionResult GetChillinDataForSierraPatron(int recordId, string lang)
        {
            var res = new PublicChillinDataConnectedToPatron();

            Response.Headers["Access-Control-Allow-Origin"] = "*";

            if (lang == null)
            {
                lang = "en";
            }

            try
            {
                res.Items = _bulkDataManager.GetChillinDataForSierraPatron(recordId, lang);
                res.Success = true;
                res.Message = "Successfully fetched data.";
            }
            catch (Exception)
            {
                res.Success = false;
                res.Message = "Failed to get data.";
            }

            return Json(res);
        }

        #region Private methods

        private Boolean DueDateIsValid(DateTime dueDate)
        {
            return dueDate != null && dueDate.Year != 1970;
        }

        #endregion

        private class PublicChillinDataConnectedToPatron
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public List<SimplifiedOrderItem> Items { get; set; }
        }
    }
}
