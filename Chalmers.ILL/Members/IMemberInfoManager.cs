using Chalmers.ILL.Models.Page;
using Microsoft.AspNetCore.Http;

namespace Chalmers.ILL.Members
{
    public interface IMemberInfoManager
    {
        int GetCurrentMemberId(HttpRequest request, HttpResponse response);

        string GetCurrentMemberText(HttpRequest request, HttpResponse response);

        string GetCurrentMemberLoginName(HttpRequest request, HttpResponse response);

        void PopulateModelWithMemberData(HttpRequest request, HttpResponse response, ChalmersILLModel model);

        void AddMemberToCache(HttpResponse response, int memberId, string memberText, string memberLoginName);

        void ClearMemberCache(HttpResponse response);
    }
}
