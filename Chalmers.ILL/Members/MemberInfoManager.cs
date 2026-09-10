using Chalmers.ILL.Models.Page;
using System;
using System.Web;

namespace Chalmers.ILL.Members
{
    public class MemberInfoManager : IMemberInfoManager
    {
        public const string cookieKey = "ChalmersILL";
        public const string memberIdKey = "memberId";
        public const string memberTextKey = "memberText";
        public const string memberLoginNameKey = "memberLoginName";

        public int GetCurrentMemberId(HttpRequestBase request, HttpResponseBase response)
        {
            var memberIdStr = request?.Cookies[cookieKey]?[memberIdKey];
            if (memberIdStr != null)
                return Convert.ToInt32(Uri.UnescapeDataString(memberIdStr));

            PopulateCookieFromCurrentUser(response);
            return 0;
        }

        public string GetCurrentMemberText(HttpRequestBase request, HttpResponseBase response)
        {
            var memberTextStr = request?.Cookies[cookieKey]?[memberTextKey];
            if (memberTextStr != null)
                return Uri.UnescapeDataString(memberTextStr);

            PopulateCookieFromCurrentUser(response);
            return HttpContext.Current?.User?.Identity?.Name ?? "";
        }

        public string GetCurrentMemberLoginName(HttpRequestBase request, HttpResponseBase response)
        {
            var memberLoginNameStr = request?.Cookies[cookieKey]?[memberLoginNameKey];
            if (memberLoginNameStr != null)
                return Uri.UnescapeDataString(memberLoginNameStr);

            PopulateCookieFromCurrentUser(response);
            return HttpContext.Current?.User?.Identity?.Name ?? "";
        }

        public void PopulateModelWithMemberData(HttpRequestBase request, HttpResponseBase response, ChalmersILLModel model)
        {
            model.CurrentMemberId = GetCurrentMemberId(request, response);
            model.CurrentMemberText = GetCurrentMemberText(request, response);
            model.CurrentMemberLoginName = GetCurrentMemberLoginName(request, response);
        }

        public void AddMemberToCache(HttpResponseBase response, int memberId, string memberText, string memberLoginName)
        {
            response.Cookies[cookieKey][memberIdKey] = Uri.EscapeDataString(Convert.ToString(memberId));
            response.Cookies[cookieKey][memberTextKey] = Uri.EscapeDataString(memberText);
            response.Cookies[cookieKey][memberLoginNameKey] = Uri.EscapeDataString(memberLoginName);
            response.Cookies[cookieKey].Expires = DateTime.Now.AddDays(1);
        }

        public void ClearMemberCache(HttpResponseBase response)
        {
            response.Cookies[cookieKey].Expires = DateTime.Now.AddDays(-1);
        }

        private void PopulateCookieFromCurrentUser(HttpResponseBase response)
        {
            var username = HttpContext.Current?.User?.Identity?.Name ?? "";
            response.Cookies[cookieKey][memberIdKey] = Uri.EscapeDataString("0");
            response.Cookies[cookieKey][memberTextKey] = Uri.EscapeDataString(username);
            response.Cookies[cookieKey][memberLoginNameKey] = Uri.EscapeDataString(username);
        }
    }
}
