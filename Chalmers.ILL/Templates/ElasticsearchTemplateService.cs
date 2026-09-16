using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Nest;

namespace Chalmers.ILL.Templates
{
    public class ElasticsearchTemplateService : TemplateServiceBase
    {
        IChillinConfiguration _config;
        IElasticClient _elasticClient;

        public ElasticsearchTemplateService(IChillinConfiguration config, IElasticClient elasticClient)
        {
            _config = config;
            _elasticClient = elasticClient;
        }

        protected override IEnumerable<Template> LoadAllTemplates()
        {
            var response = _elasticClient.Search<Template>(s => s
                .From(0)
                .Size(10000)
                .Index(_config.ElasticSearchTemplatesIndex)
                .AllTypes()
                .Query(q => q.MatchAll()));
            return response.Hits.Select(x => x.Source);
        }

        protected override void SaveTemplate(Template template)
        {
            _elasticClient.Index(template, i => i
                .Index(_config.ElasticSearchTemplatesIndex)
                .Type("_doc")
                .Id(template.Id));
        }
    }
}
