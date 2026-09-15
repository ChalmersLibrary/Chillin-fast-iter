using Chalmers.ILL.Connections;
using Chalmers.ILL.Exceptions;
using Chalmers.ILL.Models;
using Chalmers.ILL.Patron;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;

namespace Chalmers.ILL.Repositories
{
    public class FolioRepository : IFolioRepository
    {
        private IFolioConnection _folioConnection;

        public FolioRepository(IFolioConnection folioConnection) 
        {
            _folioConnection = folioConnection;
        }

        public string ByQuery(string path) =>
            GetDataFromFolioWithRetries(new FolioRequest(path));

        public string Post(string path, string body) =>
            GetDataFromFolioWithRetries(new FolioRequest(path, "POST", body));

        public string Put(string path, string body) =>
            GetDataFromFolioWithRetries(new FolioRequest(path, "PUT", body, "text/plain"));

        private string GetDataFromFolioWithRetries(FolioRequest folioRequest)
        {
            dynamic res = null;
            var retry = true;
            var retryCount = 1;
            while (retry)
            {
                try
                {
                    res = GetDataFromFolio(folioRequest);
                    retry = false;
                }
                catch (InvalidTokenException)
                {
                    _folioConnection.ClearToken(); // Force set token on retry
                    retry = retryCount > 0;

                    if (!retry)
                    {
                        // We throw for good measure if it is not retry time
                        throw;
                    }
                }
                catch (WebException e)
                {
                    var httpWebResponse = e.Response as HttpWebResponse;
                    if (httpWebResponse != null && httpWebResponse.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _folioConnection.ClearToken(); // Force set token on retry
                        retry = retryCount > 0;
                    }
                    else if (e.Status == WebExceptionStatus.SecureChannelFailure)
                    {
                        retry = retryCount > 0;
                    }
                    else
                    {
                        throw;
                    }

                    if (!retry)
                    {
                        // We throw for good measure if it is not retry time
                        throw;
                    }
                }
                retryCount -= 1;
            }
            return res;
        }

        private string GetDataFromFolio(FolioRequest folioRequest)
        {
            if (_folioConnection.NeedNewToken())
            {
                _folioConnection.SetToken();
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_folioConnection.GetFolioApiBaseAddress() + folioRequest.Path);

            System.Net.ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            request.Accept = folioRequest.Accept;
            request.ContentType = "application/json";
            request.Headers["x-okapi-tenant"] = _folioConnection.GetTenant();
            request.Headers["x-okapi-token"] = _folioConnection.GetToken();
            request.Method = folioRequest.Method;

            if (string.IsNullOrEmpty(folioRequest.Body) == false)
            {
                UTF8Encoding encoding = new UTF8Encoding();
                var bodyBytes = encoding.GetBytes(folioRequest.Body);
                request.ContentLength = bodyBytes.Length;
                var requestStream = request.GetRequestStream();
                requestStream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();

            if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
            {
                var outputStream = response.GetResponseStream();
                var sr = new StreamReader(outputStream);
                var resultString = sr.ReadToEnd();

                if (resultString.ToLower().Contains("invalid token"))
                {
                    throw new InvalidTokenException();
                }

                return resultString;
            }
            else
            {
                throw new FolioRequestException();
            }
        }
    }
}