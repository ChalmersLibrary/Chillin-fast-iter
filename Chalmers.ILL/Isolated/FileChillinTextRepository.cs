using System.IO;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.Models.PartialPage.Settings;
using Chalmers.ILL.Repositories;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // File-based IChillinTextRepository for isolated mode (fas 6, isolerat läge steg A) - a single
    // JSON document under DataPath/chillin_text.json, mirroring the real repository's single-document
    // ES index. Unlike ChillinTextRepository.All() (which null-references if the ES index is empty),
    // a missing file returns a document with an empty ChillinText rather than crashing, since there's
    // no seeding step guaranteed to have run yet.
    public class FileChillinTextRepository : IChillinTextRepository
    {
        private const string DocumentId = "1";
        private readonly IChillinConfiguration _config;

        public FileChillinTextRepository(IChillinConfiguration config)
        {
            _config = config;
        }

        private string FilePath => Path.Combine(_config.DataPath, "chillin_text.json");

        public ChillinText ByTextField(string textField)
        {
            return All().Source;
        }

        public ChillinTextDto All()
        {
            if (!File.Exists(FilePath))
            {
                return new ChillinTextDto { Id = DocumentId, Source = new ChillinText() };
            }

            return new ChillinTextDto
            {
                Id = DocumentId,
                Source = JsonConvert.DeserializeObject<ChillinText>(File.ReadAllText(FilePath))
            };
        }

        public void Put(string id, ChillinText chillinText)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(chillinText, Formatting.Indented));
        }
    }
}
