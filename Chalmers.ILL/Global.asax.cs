using Chalmers.ILL.MediaItems;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;

namespace Chalmers.ILL
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            // Web.config declares the log4net config section (configSource="config\log4net.config"),
            // but nothing ever called Configure() after Umbraco's boot sequence — which used to do
            // this — was removed. Without it, every log4net.LogManager.GetLogger(...) call in the
            // app writes nowhere.
            log4net.Config.XmlConfigurator.Configure();

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            ViewEngineConfig.RegisterViewEngines(ViewEngines.Engines);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}