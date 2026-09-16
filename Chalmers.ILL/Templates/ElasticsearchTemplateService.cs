using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Chalmers.ILL.Models;
using Nest;
using Chalmers.ILL.Configuration;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Chalmers.ILL.Templates
{
    public class ElasticsearchTemplateService : ITemplateService
    {
        IChillinConfiguration _config;
        IElasticClient _elasticClient;

        public ElasticsearchTemplateService(IChillinConfiguration config, IElasticClient elasticClient)
        {
            _config = config;
            _elasticClient = elasticClient;
        }

        public IList<Template> GetManualTemplates()
        {
            var res = SimpleSearch("automatic:false").ToList();
            res.Sort((x1, x2) => String.Compare(x1.Description, x2.Description, true, new CultureInfo("sv-se")));
            return res;
        }

        public string GetTemplateData(int nodeId)
        {
            var res = "";
            var templates = SimpleSearch("id:" + nodeId);
            if (templates.Count() > 0)
            {
                res = templates.First().Data;
            }
            else
            {
                throw new TemplateServiceException("Hittade ingen mall med ID=" + nodeId + ".");
            }

            return res;
        }

        public string GetTemplateData(string nodeName)
        {
            var res = "";
            var templates = SimpleSearch("nodeName:" + nodeName);
            if (templates.Count() > 0)
            {
                res = templates.First().Data;
            }
            else
            {
                throw new TemplateServiceException("Hittade ingen mall med nodnamn=" + nodeName + ".");
            }

            return res;
        }

        public string GetTemplateData(int templateId, OrderItemModel orderItem)
        {
            var res = "Misslyckades med att ladda mall...";
            var templates = SimpleSearch("id:" + templateId);
            if (templates.Count() > 0)
            {
                var template = templates.First();
                res = ReplaceMoustaches(template.NodeName, template.Data, orderItem);
            }

            return res;
        }

        public string GetTemplateData(string nodeName, OrderItemModel orderItem)
        {
            return ReplaceMoustaches(nodeName, GetTemplateData(nodeName), orderItem);
        }

        public void SetTemplateData(int nodeId, string data)
        {
            var templates = SimpleSearch("id:" + nodeId);

            if (templates.Count() > 0)
            {
                var template = templates.First();
                template.Data = data;
                var result = _elasticClient.Index(template, i => i
                    .Index(_config.ElasticSearchTemplatesIndex)
                    .Type("_doc")
                    .Id(nodeId));
            }
        }

        public void CreateTemplate(string description, bool acquisition)
        {
            var id = Guid.NewGuid();

            var response = _elasticClient.Search<Template>(s => s
                .Size(0)
                .Index(_config.ElasticSearchTemplatesIndex)
                .AllTypes()
                .Query(q => q.MatchAll())
                .Aggregations(a => a.Max("max_id", am => am.Field("id"))));
            var maxIdValueAggregate = response.Aggregations["max_id"] as ValueAggregate;
            var highestIdString = maxIdValueAggregate.Value.Value.ToString();
            var random = new Random();
            var nextIdString = Int32.Parse(highestIdString) + random.Next(0, 10);


            var newTemplate = new Template();
            newTemplate.Id = nextIdString;
            newTemplate.NodeName = "NodeName" + nextIdString;
            newTemplate.CreateDate = DateTime.Now;
            newTemplate.UpdateDate = DateTime.Now;
            newTemplate.NodeTypeAlias = "ChalmersILLTemplate";
            newTemplate.Description = description;
            newTemplate.Data = "";
            newTemplate.Automatic = false;
            newTemplate.Acquisition = acquisition;

            _elasticClient.Index(newTemplate, i => i
                .Index(_config.ElasticSearchTemplatesIndex)
                .Type("_doc")
                .Id(nextIdString));
        }

        public List<Template> PopulateTemplateList(List<Template> list)
        {
            list.AddRange(GetAllTemplates());

            list.Sort((x1, x2) => String.Compare(x1.Description, x2.Description, true, new CultureInfo("sv-se")));

            return list;
        }

        public string GetPrettyLibraryNameFromLibraryAbbreviation(string libraryName)
        {
            var res = OrderItemModel.LIBRARY_UNKNOWN_PRETTY_STRING;
            if (libraryName != null && libraryName.Contains("hbib"))
            {
                res = OrderItemModel.LIBRARY_Z_PRETTY_STRING;
            }
            else if (libraryName != null && libraryName.Contains("lbib"))
            {
                res = OrderItemModel.LIBRARY_ZL_PRETTY_STRING;
            }
            else if (libraryName != null && libraryName.Contains("abib"))
            {
                res = OrderItemModel.LIBRARY_ZA_PRETTY_STRING;
            }
            return res;
        }

        public string ReplaceMoustaches(string templateName, string templateString, OrderItemModel orderItem)
        {
            var template = new StringBuilder(templateString);

            // Search for double moustaches in the template and replace these with the correct order item property value.
            var moustachePattern = new Regex("{{([a-zA-Z0-9:]+)}}");
            var matches = moustachePattern.Matches(template.ToString());

            foreach (Match match in matches)
            {
                var property = match.Groups[1].Value;

                if (property.StartsWith("T:")) // Other templates that should be injected.
                {
                    var injectedTemplateName = property.Split(':').Last() + "Template";
                    if (injectedTemplateName == templateName) // Do not allow injection of template into itself.
                    {
                        template.Replace("{{" + property + "}}", "{{Injection of template into itself is not allowed}}");
                    }
                    else
                    {
                        template.Replace("{{" + property + "}}", GetTemplateData(injectedTemplateName, orderItem));
                    }
                }
                else if (property.StartsWith("S:")) // Special variables that exists in awkward places.
                {
                    var varName = property.Split(':').Last();
                    if (varName == "HomeLibrary")
                    {
                        template.Replace("{{" + property + "}}", GetPrettyLibraryNameFromLibraryAbbreviation(orderItem.SierraInfo.home_library));
                    }
                }
                else
                {
                    var value = orderItem.GetType().GetProperty(property).GetValue(orderItem);

                    if (value is DateTime)
                    {
                        template.Replace("{{" + property + "}}", ((DateTime)value).ToString("yyyy-MM-dd"));
                    }
                    else
                    {
                        template.Replace("{{" + property + "}}", value.ToString());
                    }
                }
            }

            return template.ToString();
        }

        #region Private methods

        private IEnumerable<Template> SimpleSearch(string query)
        {
            return _elasticClient.Search<Template>(s => s
                .From(0)
                .Size(10000)
                .Index(_config.ElasticSearchTemplatesIndex)
                .AllTypes()
                .Query(q => q
                    .Bool(b =>
                        b.Must(m =>
                            m.QueryString(qs =>
                                qs.DefaultField("_all")
                                .Query(query)))))).Hits.Select(x => x.Source);
        }

        private IEnumerable<Template> GetAllTemplates()
        {
            var response = _elasticClient.Search<Template>(s => s
                .From(0)
                .Size(10000)
                .Index(_config.ElasticSearchTemplatesIndex)
                .AllTypes()
                .Query(q => q.MatchAll()));
            return response.Hits.Select(x => x.Source);
        }

        #endregion
    }
}