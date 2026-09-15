using System;
using Chalmers.ILL.Members;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Members
{
    // Was a single "ChalmersILL" cookie with three HttpCookie subkeys; rewritten (fas 2) as
    // three separate cookies since ASP.NET Core cookies have no subkey concept - see the
    // comment on MemberInfoManager. The old subkey wire-format vulnerability
    // (Uri.EscapeUriString not escaping '&'/'='/';', the delimiters HttpCookie used to
    // multiplex subkeys) no longer applies structurally, since there's nothing left to
    // multiplex - each cookie carries exactly one Uri.EscapeDataString-encoded value.
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
        public void AddMemberToCache_StoresIdTextAndLoginNameInCookies()
        {
            var httpContext = new DefaultHttpContext();

            _manager.AddMemberToCache(httpContext.Response, 42, "John Doe", "john.doe");

            var request = RequestFromResponse(httpContext);
            Assert.AreEqual(42, _manager.GetCurrentMemberId(request, httpContext.Response));
            Assert.AreEqual("John Doe", _manager.GetCurrentMemberText(request, httpContext.Response));
            Assert.AreEqual("john.doe", _manager.GetCurrentMemberLoginName(request, httpContext.Response));
        }

        [TestMethod]
        public void AddMemberToCache_SetsCookieExpiryAboutOneDayAhead()
        {
            var httpContext = new DefaultHttpContext();

            _manager.AddMemberToCache(httpContext.Response, 1, "Test", "test");

            var setCookie = httpContext.Response.Headers["Set-Cookie"].ToString();
            StringAssert.Contains(setCookie, "expires=");
            var expiresPart = setCookie.Split(new[] { "expires=" }, StringSplitOptions.None)[1].Split(';')[0];
            var expiry = DateTimeOffset.Parse(expiresPart);
            Assert.IsTrue(expiry > DateTimeOffset.Now.AddHours(23));
            Assert.IsTrue(expiry < DateTimeOffset.Now.AddHours(25));
        }

        [TestMethod]
        public void GetCurrentMemberId_ReturnsZeroWhenNoCookiePresent()
        {
            var httpContext = new DefaultHttpContext();

            var result = _manager.GetCurrentMemberId(httpContext.Request, httpContext.Response);

            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void GetCurrentMemberText_ReturnsEmptyStringWhenNoCookiePresent()
        {
            var httpContext = new DefaultHttpContext();

            var result = _manager.GetCurrentMemberText(httpContext.Request, httpContext.Response);

            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void GetCurrentMemberLoginName_ReturnsEmptyStringWhenNoCookiePresent()
        {
            var httpContext = new DefaultHttpContext();

            var result = _manager.GetCurrentMemberLoginName(httpContext.Request, httpContext.Response);

            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void ClearMemberCache_ExpiresCookiesInThePast()
        {
            var httpContext = new DefaultHttpContext();
            _manager.AddMemberToCache(httpContext.Response, 5, "User", "user");

            _manager.ClearMemberCache(httpContext.Response);

            var setCookieHeaders = httpContext.Response.Headers["Set-Cookie"].ToString();
            StringAssert.Contains(setCookieHeaders, "expires=Thu, 01 Jan 1970");
        }

        [TestMethod]
        public void AddMemberToCache_MemberTextContainsCookieDelimiters_SurvivesWireRoundTrip()
        {
            // Regression coverage for the fas 0a bug (Uri.EscapeUriString didn't escape
            // '&'/'='/';') - round-trips through the real Set-Cookie -> Cookie header wire
            // format instead of copying values directly, so it would catch the same class of
            // encoding bug if it recurred.
            var httpContext = new DefaultHttpContext();

            _manager.AddMemberToCache(httpContext.Response, 1, "A&B;C=D", "user");
            var request = RequestFromResponse(httpContext);

            var result = _manager.GetCurrentMemberText(request, httpContext.Response);

            Assert.AreEqual("A&B;C=D", result);
        }

        // Simulates a subsequent request by parsing this response's Set-Cookie headers into a
        // new request's Cookie header - the same wire hop a browser would do.
        private static HttpRequest RequestFromResponse(DefaultHttpContext httpContext)
        {
            var nextContext = new DefaultHttpContext();
            foreach (var setCookie in httpContext.Response.Headers["Set-Cookie"])
            {
                var nameValue = setCookie.Split(';')[0];
                nextContext.Request.Headers.Append("Cookie", nameValue);
            }
            return nextContext.Request;
        }
    }
}
