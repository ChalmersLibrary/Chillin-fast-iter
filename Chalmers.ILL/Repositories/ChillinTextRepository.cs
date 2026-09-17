using Chalmers.ILL.Models;
using Chalmers.ILL.Models.PartialPage.Settings;
using Nest;
using System.Linq;

namespace Chalmers.ILL.Repositories
{
    public class ChillinTextRepository : IChillinTextRepository
    {
        private readonly string Index = "chillin_text";
        private readonly string Type = "chillinText";
        private readonly IElasticClient _elasticClient;

        public ChillinTextRepository(IElasticClient elasticClient)
        {
            _elasticClient = elasticClient;
        }

        public ChillinText ByTextField(string textField)
        {
            var response = _elasticClient.Search<ChillinText>(x => x
                .Index(Index)
                .Type(Type)
                .Size(1)
                .Query(q => q.Bool(b => b.Must(m => m.Exists(e => e.Field(textField)))))
                .Source(s => s.Includes(i => i.Field(textField))));
            // An empty index (e.g. a freshly set-up environment, before the first Put) is a real
            // state, not an error - .First() used to throw InvalidOperationException here (fas 0a
            // latent defect). Mirrors Isolated.FileChillinTextRepository's "missing document -> empty
            // ChillinText" contract instead of crashing.
            return response.Documents.FirstOrDefault() ?? new ChillinText();
        }

        public ChillinTextDto All()
        {
            var response = _elasticClient.Search<ChillinText>(x => x
                 .Index(Index)
                 .Type(Type)
                 .Size(1)
                 .Query(q => q.MatchAll()));
            // Same empty-index case as ByTextField above - hit used to be null-dereferenced here.
            IHit<ChillinText> hit = response.Hits.FirstOrDefault();
            return new ChillinTextDto
            {
                Id = hit?.Id,
                Source = hit?.Source ?? new ChillinText()
            };
        }

        public void Put(string id, ChillinText chillinText)
        {
            _elasticClient.Index(chillinText, x => x
                .Index(Index)
                .Type(Type)
                .Id(id)
                .Refresh(new Elasticsearch.Net.Refresh()));
        }
    }
}