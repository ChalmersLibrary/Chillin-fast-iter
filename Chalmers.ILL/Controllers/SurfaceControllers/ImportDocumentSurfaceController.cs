using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Chalmers.ILL.Models;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Chalmers.ILL.OrderItems;
using Chalmers.ILL.MediaItems;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    [Authorize]
    public class ImportDocumentSurfaceController : Controller
    {
        public static int EVENT_TYPE { get { return 16; } }

        IOrderItemManager _orderItemManager;
        IMediaItemManager _mediaItemManager;

        public ImportDocumentSurfaceController(IOrderItemManager orderItemManager, IMediaItemManager mediaItemManager)
        {
            _orderItemManager = orderItemManager;
            _mediaItemManager = mediaItemManager;
        }

        /// <summary>
        /// Import a document from a given URL and store it as a media item bound to the given order item.
        /// </summary>
        /// <param name="orderItemNodeId">The id of the order item which the document should be bound to.</param>
        /// <param name="url">The url from which we should fetch the document.</param>
        /// <returns>Returns a result indicating how the request went.</returns>
        [HttpPost]
        public ActionResult ImportFromUrl(int orderItemNodeId, string url)
        {
            // Json response
            var json = new ResultResponse();

            Stream stream = null;

            try
            {
                HttpWebRequest fileReq = (HttpWebRequest)HttpWebRequest.Create(url);
                fileReq.CookieContainer = new CookieContainer();
                fileReq.AllowAutoRedirect = true;
                HttpWebResponse fileResp = (HttpWebResponse)fileReq.GetResponse();
                stream = fileResp.GetResponseStream();

                var orderItem = _orderItemManager.GetOrderItem(orderItemNodeId);
                var cdPattern = new Regex("filename=\"?(.*)\"?(?:;|$)");
                var fileEndingPattern = new Regex("(?i)\\.(?:pdf|txt|tif|tiff)$");
                string name = "";
                if (fileEndingPattern.IsMatch(url))
                {
                    name = fileResp.ResponseUri.AbsoluteUri.Substring(fileResp.ResponseUri.AbsoluteUri.LastIndexOf("/") + 1);
                }
                else
                {
                    name = cdPattern.Match(fileResp.Headers["Content-Disposition"]).Groups[1].Value;
                }

                if (String.IsNullOrEmpty(name))
                {
                    throw new Exception("Not a valid document.");
                }
                else
                {
                    var savedMediaItem = _mediaItemManager.CreateMediaItem(name, orderItem.NodeId, orderItem.OrderId, stream, fileResp.ContentType);

                    var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                    _orderItemManager.AddExistingMediaItemAsAnAttachment(orderItem.NodeId, savedMediaItem.Id, name, savedMediaItem.Url, eventId);

                    json.Success = true;
                    json.Message = "Document imported successfully.";
                }
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Failed to import document: " + e.Message;
            }

            return Json(json);
        }

        /// <summary>
        /// Import a document from data and store it as a media item bound to the given order item.
        /// </summary>
        /// <param name="orderItemNodeId">The id of the order item which the document should be bound to.</param>
        /// <param name="filename">The filename of the original file, which we use as name for the document.</param>
        /// <param name="data">The base 64 encoded data which we should use to create the document.</param>
        /// <returns>Returns a result indicating how the request went.</returns>
        [HttpPost]
        public ActionResult ImportFromData(int orderItemNodeId, string filename, string data)
        {
            // Json response
            var json = new ResultResponse();

            try
            {
                if (String.IsNullOrEmpty(filename))
                {
                    throw new Exception("Not a valid document.");
                }
                else
                {
                    var orderItem = _orderItemManager.GetOrderItem(orderItemNodeId);

                    var dataArray = Regex.Split(data, "base64[;,]");
                    var pattern = new Regex("^data:(.*)[,;]$");
                    var contentType = pattern.Match(dataArray[0]).Groups[1].Value;

                    var savedMediaItem = _mediaItemManager.CreateMediaItem(filename, orderItem.NodeId, orderItem.OrderId, new MemoryStream(Convert.FromBase64String(dataArray[1])), contentType);

                    var eventId = _orderItemManager.GenerateEventId(EVENT_TYPE);
                    _orderItemManager.AddExistingMediaItemAsAnAttachment(orderItem.NodeId, savedMediaItem.Id, filename, savedMediaItem.Url, eventId);

                    json.Success = true;
                    json.Message = savedMediaItem.Id.ToString() + ";" + savedMediaItem.Url;
                }
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Failed to import document from data: " + e.Message;
            }

            return Json(json);
        }
    }
}