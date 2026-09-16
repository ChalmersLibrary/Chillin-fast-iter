using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chalmers.ILL.Configuration
{
    // Was IConfiguration. Renamed in fas 6: the app is moving to
    // Microsoft.Extensions.Configuration, whose own IConfiguration would collide with this one on
    // every file that needs both - and worse, would silently resolve to the wrong one in files that
    // only `using Microsoft.Extensions.Configuration`. This is app configuration, not the framework
    // abstraction, so it gets the distinct name.
    public interface IChillinConfiguration
    {
        bool UseMicrosoftGraphMailService { get; }
        string MicrosoftGraphApiUserId { get; }
        string MicrosoftGraphApiEndpoint { get; }
        string MicrosoftGraphAuthority { get; }
        string MicrosoftGraphClientId { get; }
        string MicrosoftGraphClientSecret { get; }
        string StorageConnectionString { get; }
        string ElasticSearchUrl { get; }
        string ElasticSearchIndex { get; }
        string ElasticSearchTemplatesIndex { get; }
        string BaseUrl { get; }
        string LibPSearchUrl { get; }
        string LibPSearchApiKey { get; }
    }
}
