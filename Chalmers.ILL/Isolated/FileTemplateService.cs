using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.Models;
using Chalmers.ILL.Templates;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // File-based ITemplateService for isolated mode (fas 6, isolerat läge steg A) - one JSON file
    // per template under DataPath/templates, named by Id. Everything except loading/saving is
    // shared logic in TemplateServiceBase (filtering, sorting, moustache substitution).
    public class FileTemplateService : TemplateServiceBase
    {
        private readonly IChillinConfiguration _config;

        public FileTemplateService(IChillinConfiguration config)
        {
            _config = config;
        }

        private string TemplatesDirectory => Path.Combine(_config.DataPath, "templates");
        private string TemplatePath(int id) => Path.Combine(TemplatesDirectory, id + ".json");

        protected override IEnumerable<Template> LoadAllTemplates()
        {
            if (!Directory.Exists(TemplatesDirectory))
                return System.Array.Empty<Template>();

            return Directory.GetFiles(TemplatesDirectory, "*.json")
                .Select(f => JsonConvert.DeserializeObject<Template>(File.ReadAllText(f)))
                .ToList();
        }

        protected override void SaveTemplate(Template template)
        {
            Directory.CreateDirectory(TemplatesDirectory);
            File.WriteAllText(TemplatePath(template.Id), JsonConvert.SerializeObject(template, Formatting.Indented));
        }
    }
}
