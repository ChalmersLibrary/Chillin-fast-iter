using System;
using System.Web;
using Chalmers.ILL.Members;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Members
{
    [TestClass]
    public class MemberInfoManagerTest
    {
        MemberInfoManager _manager;

        [TestInitialize]
        public void Setup()
        {
            _manager = new MemberInfoManager();
        }

        [TestMethod]
        public void AddMemberToCache_StoresIdTextAndLoginNameInCookie()
        {
            var response = new FakeHttpResponse();

            _manager.AddMemberToCache(response, 42, "John Doe", "john.doe");

            var cookie = response.Cookies[MemberInfoManager.cookieKey];
            Assert.AreEqual("42", Uri.UnescapeDataString(cookie[MemberInfoManager.memberIdKey]));
            Assert.AreEqual("John Doe", Uri.UnescapeDataString(cookie[MemberInfoManager.memberTextKey]));
            Assert.AreEqual("john.doe", Uri.UnescapeDataString(cookie[MemberInfoManager.memberLoginNameKey]));
        }

        [TestMethod]
        public void AddMemberToCache_SetsCookieExpiryAboutOneDayAhead()
        {
            var response = new FakeHttpResponse();

            _manager.AddMemberToCache(response, 1, "Test", "test");

            var expiry = response.Cookies[MemberInfoManager.cookieKey].Expires;
            Assert.IsTrue(expiry > DateTime.Now.AddHours(23));
            Assert.IsTrue(expiry < DateTime.Now.AddHours(25));
        }

        [TestMethod]
        public void GetCurrentMemberId_ReturnsMemberIdFromCookie()
        {
            var request = new FakeHttpRequest(BuildRequestCookies(99, "Test User", "test.user"));

            var result = _manager.GetCurrentMemberId(request, new FakeHttpResponse());

            Assert.AreEqual(99, result);
        }

        [TestMethod]
        public void GetCurrentMemberText_ReturnsMemberTextFromCookie()
        {
            var request = new FakeHttpRequest(BuildRequestCookies(1, "Jane Smith", "jane.smith"));

            var result = _manager.GetCurrentMemberText(request, new FakeHttpResponse());

            Assert.AreEqual("Jane Smith", result);
        }

        [TestMethod]
        public void GetCurrentMemberLoginName_ReturnsMemberLoginNameFromCookie()
        {
            var request = new FakeHttpRequest(BuildRequestCookies(1, "Jane Smith", "jane.smith"));

            var result = _manager.GetCurrentMemberLoginName(request, new FakeHttpResponse());

            Assert.AreEqual("jane.smith", result);
        }

        [TestMethod]
        public void GetCurrentMemberId_ReturnsZeroWhenNoCookiePresent()
        {
            // No HttpContext in unit tests, so fallback returns 0.
            var request = new FakeHttpRequest(new HttpCookieCollection());

            var result = _manager.GetCurrentMemberId(request, new FakeHttpResponse());

            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetCurrentMemberText_ReturnsEmptyStringWhenNoCookiePresent()
        {
            var request = new FakeHttpRequest(new HttpCookieCollection());

            var result = _manager.GetCurrentMemberText(request, new FakeHttpResponse());

            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void GetCurrentMemberLoginName_ReturnsEmptyStringWhenNoCookiePresent()
        {
            var request = new FakeHttpRequest(new HttpCookieCollection());

            var result = _manager.GetCurrentMemberLoginName(request, new FakeHttpResponse());

            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void ClearMemberCache_ExpiresCookieInThePast()
        {
            var response = new FakeHttpResponse();
            _manager.AddMemberToCache(response, 5, "User", "user");

            _manager.ClearMemberCache(response);

            Assert.IsTrue(response.Cookies[MemberInfoManager.cookieKey].Expires < DateTime.Now);
        }

        [TestMethod]
        public void GetCurrentMemberId_ReturnsValueStoredByAddMemberToCache()
        {
            // Simulates a subsequent request: copy response cookie to new request.
            var response = new FakeHttpResponse();
            _manager.AddMemberToCache(response, 7, "Alice", "alice");
            var request = new FakeHttpRequest(CopyToRequestCookies(response));

            var result = _manager.GetCurrentMemberId(request, new FakeHttpResponse());

            Assert.AreEqual(7, result);
        }

        [TestMethod]
        public void AddMemberToCache_MemberTextContainsCookieDelimiters_SurvivesWireRoundTrip()
        {
            // HttpCookie multiplexes subkeys into a single "key1=val1&key2=val2" string on the
            // wire (via the Value property) and re-splits on '&'/'=' when parsed back from a real
            // request. Uri.EscapeUriString does not escape those characters, so a memberText
            // containing them used to corrupt the cookie. Round-tripping through cookie.Value
            // (instead of copying subkey objects directly) reproduces that wire format.
            var response = new FakeHttpResponse();
            _manager.AddMemberToCache(response, 1, "A&B;C=D", "user");
            var wireValue = response.Cookies[MemberInfoManager.cookieKey].Value;

            var parsedCookie = new HttpCookie(MemberInfoManager.cookieKey) { Value = wireValue };
            var requestCookies = new HttpCookieCollection();
            requestCookies.Add(parsedCookie);
            var request = new FakeHttpRequest(requestCookies);

            var result = _manager.GetCurrentMemberText(request, new FakeHttpResponse());

            Assert.AreEqual("A&B;C=D", result);
        }

        // --- helpers ---

        private HttpCookieCollection BuildRequestCookies(int memberId, string memberText, string memberLoginName)
        {
            var cookie = new HttpCookie(MemberInfoManager.cookieKey);
            cookie[MemberInfoManager.memberIdKey] = Uri.EscapeUriString(Convert.ToString(memberId));
            cookie[MemberInfoManager.memberTextKey] = Uri.EscapeUriString(memberText);
            cookie[MemberInfoManager.memberLoginNameKey] = Uri.EscapeUriString(memberLoginName);
            var collection = new HttpCookieCollection();
            collection.Add(cookie);
            return collection;
        }

        private HttpCookieCollection CopyToRequestCookies(FakeHttpResponse response)
        {
            var src = response.Cookies[MemberInfoManager.cookieKey];
            var cookie = new HttpCookie(MemberInfoManager.cookieKey);
            cookie[MemberInfoManager.memberIdKey] = src[MemberInfoManager.memberIdKey];
            cookie[MemberInfoManager.memberTextKey] = src[MemberInfoManager.memberTextKey];
            cookie[MemberInfoManager.memberLoginNameKey] = src[MemberInfoManager.memberLoginNameKey];
            var collection = new HttpCookieCollection();
            collection.Add(cookie);
            return collection;
        }

        // HttpRequestBase and HttpResponseBase have all-virtual methods — only Cookies is needed here.

        class FakeHttpRequest : HttpRequestBase
        {
            private readonly HttpCookieCollection _cookies;
            public FakeHttpRequest(HttpCookieCollection cookies) { _cookies = cookies; }
            public override HttpCookieCollection Cookies => _cookies;
        }

        class FakeHttpResponse : HttpResponseBase
        {
            // Pre-create the application cookie so subkey writes don't throw NullReferenceException,
            // matching how ASP.NET's real HttpResponse.Cookies auto-creates on first access.
            private readonly HttpCookieCollection _cookies = new HttpCookieCollection();
            public FakeHttpResponse() { _cookies.Add(new HttpCookie(MemberInfoManager.cookieKey)); }
            public override HttpCookieCollection Cookies => _cookies;
        }
    }
}
