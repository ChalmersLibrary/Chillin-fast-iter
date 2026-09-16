using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Chalmers.ILL.Models;

namespace Chalmers.ILL.Templates
{
    // Everything ITemplateService does beyond "load every template" and "save one template" is
    // pure logic (filtering, sorting, moustache substitution) that was previously duplicated
    // whenever a new backing store was added. Introduced in fas 6 (isolerat läge steg A) so
    // ElasticsearchTemplateService and Isolated.FileTemplateService share it instead of drifting -
    // an abstract base rather than a helper class because ReplaceMoustaches recursively calls back
    // into GetTemplateData (for "{{T:...}}" template injection), so it can't be a one-way utility.
    public abstract class TemplateServiceBase : ITemplateService
    {
        protected abstract IEnumerable<Template> LoadAllTemplates();
        protected abstract void SaveTemplate(Template template);

        public IList<Template> GetManualTemplates()
        {
            var res = LoadAllTemplates().Where(t => !t.Automatic).ToList();
            res.Sort((x1, x2) => String.Compare(x1.Description, x2.Description, true, new CultureInfo("sv-se")));
            return res;
        }

        public string GetTemplateData(int nodeId)
        {
            var template = LoadAllTemplates().FirstOrDefault(t => t.Id == nodeId);
            if (template == null)
                throw new TemplateServiceException("Hittade ingen mall med ID=" + nodeId + ".");

            return template.Data;
        }

        public string GetTemplateData(string nodeName)
        {
            var template = LoadAllTemplates().FirstOrDefault(t => t.NodeName == nodeName);
            if (template == null)
                throw new TemplateServiceException("Hittade ingen mall med nodnamn=" + nodeName + ".");

            return template.Data;
        }

        public string GetTemplateData(int templateId, OrderItemModel orderItem)
        {
            var template = LoadAllTemplates().FirstOrDefault(t => t.Id == templateId);
            if (template == null)
                return "Misslyckades med att ladda mall...";

            return ReplaceMoustaches(template.NodeName, template.Data, orderItem);
        }

        public string GetTemplateData(string nodeName, OrderItemModel orderItem)
        {
            return ReplaceMoustaches(nodeName, GetTemplateData(nodeName), orderItem);
        }

        public void SetTemplateData(int nodeId, string data)
        {
            var template = LoadAllTemplates().FirstOrDefault(t => t.Id == nodeId);
            if (template != null)
            {
                template.Data = data;
                SaveTemplate(template);
            }
        }

        public void CreateTemplate(string description, bool acquisition)
        {
            var all = LoadAllTemplates().ToList();
            var highestId = all.Count > 0 ? all.Max(t => t.Id) : 0;
            var random = new Random();
            var nextId = highestId + random.Next(0, 10);

            var newTemplate = new Template
            {
                Id = nextId,
                NodeName = "NodeName" + nextId,
                CreateDate = DateTime.Now,
                UpdateDate = DateTime.Now,
                NodeTypeAlias = "ChalmersILLTemplate",
                Description = description,
                Data = "",
                Automatic = false,
                Acquisition = acquisition
            };

            SaveTemplate(newTemplate);
        }

        public List<Template> PopulateTemplateList(List<Template> list)
        {
            list.AddRange(LoadAllTemplates());
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
    }
}
