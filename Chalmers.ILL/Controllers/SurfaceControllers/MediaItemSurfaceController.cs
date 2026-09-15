using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class MediaItemSurfaceController : Controller
    {
        private IMediaItemManager _mediaItemManager;

        public MediaItemSurfaceController(IMediaItemManager mediaItemManager)
        {
            _mediaItemManager = mediaItemManager;
        }

        [HttpGet]
        public ActionResult GetMediaItem(string id)
        {
            ActionResult res = Json(new ResultResponse(false, "Unknown error."));

            try
            {
                var storedMediaItem = _mediaItemManager.GetOne(id);
                if (storedMediaItem != null)
                {
                    var cd = new System.Net.Mime.ContentDisposition
                    {
                        FileName = storedMediaItem.Name,
                        Inline = true
                    };
                    Response.Headers["Content-Disposition"] = cd.ToString();
                    res = File(storedMediaItem.Data, storedMediaItem.ContentType);
                }
                else
                {
                    res = Json(new ResultResponse(false, "Couldn't find stored media item with ID = " + id + "."));
                }
            }
            catch (Exception e)
            {
                res = Json(new ResultResponse(false, "Error: " + e.Message));
            }

            return res;
        }
    }
}