using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Http;
using System;

namespace Chalmers.ILL.Members
{
    public class MemberInfoManager : IMemberInfoManager
    {
        // Was a single "ChalmersILL" cookie with three System.Web HttpCookie subkeys
        // (memberId/memberText/memberLoginName). ASP.NET Core cookies have no subkey concept,
        // so this is now three separate cookies - a wire-format change, but the same data is
        // available to the same call sites. Existing sessions are invalidated on deploy (users
        // re-login), same as any auth-cookie format change.
        public const string memberIdCookieKey = "ChalmersILL_memberId";
        public const string memberTextCookieKey = "ChalmersILL_memberText";
        public const string memberLoginNameCookieKey = "ChalmersILL_memberLoginName";

        public int GetCurrentMemberId(HttpRequest request, HttpResponse response)
        {
            var memberIdStr = request?.Cookies[memberIdCookieKey];
            if (memberIdStr != null)
                return Convert.ToInt32(Uri.UnescapeDataString(memberIdStr));

            PopulateCookieFromCurrentUser(request, response);
            return 0;
        }

        public string GetCurrentMemberText(HttpRequest request, HttpResponse response)
        {
            var memberTextStr = request?.Cookies[memberTextCookieKey];
            if (memberTextStr != null)
                return Uri.UnescapeDataString(memberTextStr);

            PopulateCookieFromCurrentUser(request, response);
            return request?.HttpContext?.User?.Identity?.Name ?? "";
        }

        public string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response)
        {
            var memberLoginNameStr = request?.Cookies[memberLoginNameCookieKey];
            if (memberLoginNameStr != null)
                return Uri.UnescapeDataString(memberLoginNameStr);

            PopulateCookieFromCurrentUser(request, response);
            return request?.HttpContext?.User?.Identity?.Name ?? "";
        }

        public void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, ChalmersILLModel model)
        {
            model.CurrentMemberId = GetCurrentMemberId(request, response);
            model.CurrentMemberText = GetCurrentMemberText(request, response);
            model.CurrentMemberLoginName = GetCurrentMemberLoginName(request, response);
        }

        public void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName)
        {
            var options = new CookieOptions { Expires = DateTimeOffset.Now.AddDays(1) };
            response.Cookies.Append(memberIdCookieKey, Uri.EscapeDataString(Convert.ToString(memberId)), options);
            response.Cookies.Append(memberTextCookieKey, Uri.EscapeDataString(memberText), options);
            response.Cookies.Append(memberLoginNameCookieKey, Uri.EscapeDataString(memberLoginName), options);
        }

        public void ClearMemberCache(HttpResponse response)
        {
            response.Cookies.Delete(memberIdCookieKey);
            response.Cookies.Delete(memberTextCookieKey);
            response.Cookies.Delete(memberLoginNameCookieKey);
        }

        private void PopulateCookieFromCurrentUser(HttpRequest request, HttpResponse response)
        {
            var username = request?.HttpContext?.User?.Identity?.Name ?? "";
            response.Cookies.Append(memberIdCookieKey, Uri.EscapeDataString("0"));
            response.Cookies.Append(memberTextCookieKey, Uri.EscapeDataString(username));
            response.Cookies.Append(memberLoginNameCookieKey, Uri.EscapeDataString(username));
        }
    }
}
