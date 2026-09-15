using Chalmers.ILL.Models;
using Chalmers.ILL.Repositories;
using Chalmers.ILL.Services;
using System;
using Microsoft.AspNetCore.Mvc;

namespace Chalmers.ILL.Controllers.SurfaceControllers
{
    public class ChillinTextSurfaceController : Controller
    {
        private readonly IChillinTextRepository _chillinTextRepository;
        private readonly IJsonService _jsonService;

        public ChillinTextSurfaceController(IChillinTextRepository chillinTextRepository, IJsonService jsonService)
        {
            _chillinTextRepository = chillinTextRepository;
            _jsonService = jsonService;
        }

        [HttpGet]
        public ActionResult RenderChillinTextsAction()
        {
            var pageModel = _chillinTextRepository.All();
            return PartialView("Settings/ChillinText", pageModel);
        }

        [HttpPost]
        public ActionResult Save(string id, string chillinText)
        {
            var json = new ResultResponse();
            var chillin = _jsonService.DeserializeObject<ChillinText>(chillinText);
            try
            {
                _chillinTextRepository.Put(id, new ChillinText
                {
                    CheckInNote = chillin.CheckInNote,
                    CheckOutNote = chillin.CheckOutNote,
                    StandardTitleText = chillin.StandardTitleText              
                });
                json.Success = true;
                json.Message = "Sparade ny text.";
            }
            catch (Exception e)
            {
                json.Success = false;
                json.Message = "Error: " + e.Message;
            }

            return Json(json);
        }
    }
}