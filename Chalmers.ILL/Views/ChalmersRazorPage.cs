using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Razor;

namespace Chalmers.ILL.Views
{
    // Classic System.Web.Mvc.WebViewPage exposed Request/Response as direct properties; Core's
    // RazorPage<T> only exposes them via Context.Request/Context.Response. Rather than rewrite
    // every Request/Response reference across ~10 views (fas 4), this restores the old shortcut
    // properties via a shared base class, wired up once in _ViewImports.cshtml.
    public abstract class ChalmersRazorPage<TModel> : RazorPage<TModel>
    {
        public HttpRequest Request => Context.Request;
        public HttpResponse Response => Context.Response;
    }
}
