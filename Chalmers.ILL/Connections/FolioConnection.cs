using Chalmers.ILL.Configuration;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace Chalmers.ILL.Connections
{
    public class FolioConnection : IFolioConnection
    {
        private readonly string _folioApiBaseAddress;
        private readonly string _tenant;
        private readonly string _username;
        private readonly string _password;
        private string _token;
        private DateTime _tokenExpiration;
        private string _tokenPath;
        private string _tokenDomain;
        private string _refresh;
        private string _refreshPath;
        private string _refreshDomain;
        private DateTime _refreshExpiration;

        public FolioConnection(IChillinConfiguration config)
        {
            _folioApiBaseAddress = config.FolioApiBaseAddress;
            _tenant = config.FolioXOkapiTenant;
            _username = config.FolioUsername;
            _password = config.FolioPassword;
        }

        public void ClearToken()
        {
            _token = null;
        }

        public bool NeedNewToken()
        {
            return string.IsNullOrEmpty(_token) || _tokenExpiration <= DateTime.UtcNow;
        }

        public bool IsRefreshTokenOk()
        {
            return !(string.IsNullOrEmpty(_refresh) || _refreshExpiration <= DateTime.UtcNow);
        }

        public void SetToken()
        {
            _token = null;

            if (IsRefreshTokenOk())
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(_folioApiBaseAddress + "/authn/refresh");
                    request.CookieContainer = new CookieContainer();

                    request.Accept = "application/json";
                    request.ContentType = "application/json";
                    request.Headers["x-okapi-tenant"] = _tenant;
                    request.Method = "POST";

                    System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    request.CookieContainer.Add(new Cookie("folioRefreshToken", _refresh, _refreshPath, _refreshDomain));
                    var requestStream = request.GetRequestStream();
                    requestStream.Write(new byte[0], 0, 0);

                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    _token = response.Cookies["folioAccessToken"].Value;
                    _refresh = response.Cookies["folioRefreshToken"].Value;
                    _tokenPath = response.Cookies["folioAccessToken"].Path;
                    _refreshPath = response.Cookies["folioRefreshToken"].Path;
                    _tokenDomain = response.Cookies["folioAccessToken"].Domain;
                    _refreshDomain = response.Cookies["folioRefreshToken"].Domain;
                    string content;
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        content = reader.ReadToEnd();
                    }
                    var jsonContent = JsonConvert.DeserializeObject<dynamic>(content);
                    _tokenExpiration = jsonContent.accessTokenExpiration;
                    _refreshExpiration = jsonContent.refreshTokenExpiration;
                }
                catch (Exception)
                {
                    // NOOP
                }
            }

            if (NeedNewToken())
            {
                // Looks like refresh failed or we have no refresh token, let's try a login
                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(_folioApiBaseAddress + "/authn/login-with-expiry");
                request.CookieContainer = new CookieContainer();

                request.Accept = "application/json";
                request.ContentType = "application/json";
                request.Headers["x-okapi-tenant"] = _tenant;
                request.Method = "POST";

                System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                UTF8Encoding encoding = new UTF8Encoding();
                var bodyBytes = encoding.GetBytes("{ \"username\": \"" + _username + "\", \"password\": \"" + _password + "\" }");
                request.ContentLength = bodyBytes.Length;
                var requestStream = request.GetRequestStream();
                requestStream.Write(bodyBytes, 0, bodyBytes.Length);

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                _token = response.Cookies["folioAccessToken"].Value;
                _refresh = response.Cookies["folioRefreshToken"].Value;
                _tokenPath = response.Cookies["folioAccessToken"].Path;
                _refreshPath = response.Cookies["folioRefreshToken"].Path;
                _tokenDomain = response.Cookies["folioAccessToken"].Domain;
                _refreshDomain = response.Cookies["folioRefreshToken"].Domain;
                string content;
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    content = reader.ReadToEnd();
                }
                var jsonContent = JsonConvert.DeserializeObject<dynamic>(content);
                _tokenExpiration = jsonContent.accessTokenExpiration;
                _refreshExpiration = jsonContent.refreshTokenExpiration;
            }
        }

        public string GetToken()
        {
            return _token;
        }

        public string GetTenant()
        {
            return _tenant;
        }

        public string GetFolioApiBaseAddress()
        {
            return _folioApiBaseAddress;
        }
    }
}
