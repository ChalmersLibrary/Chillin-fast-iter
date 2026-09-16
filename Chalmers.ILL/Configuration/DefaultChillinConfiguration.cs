using System;
using System.Configuration;

namespace Chalmers.ILL.Configuration
{
    public class DefaultChillinConfiguration : IChillinConfiguration
    {
        public bool UseMicrosoftGraphMailService
        {
            get
            {
                return ConfigurationManager.AppSettings["UseMicrosoftGraphMailService"] != null && ConfigurationManager.AppSettings["UseMicrosoftGraphMailService"].ToLower() == "true";
            }
        }
        public string MicrosoftGraphApiUserId
        {
            get
            {
                return ConfigurationManager.AppSettings["MicrosoftGraphApiUserId"];
            }
        }

        public string MicrosoftGraphApiEndpoint
        {
            get
            {
                return ConfigurationManager.AppSettings["MicrosoftGraphApiEndpoint"];
            }
        }

        public string MicrosoftGraphAuthority
        {
            get
            {
                return ConfigurationManager.AppSettings["MicrosoftGraphAuthority"];
            }
        }

        public string MicrosoftGraphClientId
        {
            get
            {
                return ConfigurationManager.AppSettings["MicrosoftGraphClientId"];
            }
        }

        public string MicrosoftGraphClientSecret
        {
            get
            {
                return ConfigurationManager.AppSettings["MicrosoftGraphClientSecret"];
            }
        }

        public string BaseUrl
        {
            get
            {
                return ConfigurationManager.AppSettings["BaseUrl"];
            }
        }

        public string ElasticSearchUrl
        {
            get
            {
                return ConfigurationManager.AppSettings["ElasticSearchUrl"];
            }
        }

        public string ElasticSearchIndex
        {
            get
            {
                return ConfigurationManager.AppSettings["ElasticSearchIndex"];
            }
        }

        public string ElasticSearchTemplatesIndex
        {
            get
            {
                return ConfigurationManager.AppSettings["ElasticSearchTemplatesIndex"];
            }
        }

        public string StorageConnectionString
        {
            get
            {
                return ConfigurationManager.AppSettings["BlobStorageConnectionString"];
            }
        }

        public string LibPSearchUrl
        {
            get
            {
                return ConfigurationManager.AppSettings["LibPSearchUrl"];
            }
        }

        public string LibPSearchApiKey
        {
            get
            {
                return ConfigurationManager.AppSettings["LibPSearchApiKey"];
            }
        }
    }
}