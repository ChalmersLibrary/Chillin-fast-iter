using System.Collections.Generic;
using System.Linq;
using Chalmers.ILL.Models;
using Chalmers.ILL.Templates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests.Templates
{
    // TODO-remove-dotnet-framework.md, fas 10: "Säkerställ att ICU finns i både devcontainer och
    // App Service" - GetManualTemplates()/PopulateTemplateList() sort template descriptions with
    // CultureInfo("sv-se"), which on Linux is backed by ICU rather than Windows' NLS. Without ICU
    // (or with InvariantGlobalization=true, a common image-size reflex), the comparison silently
    // falls back to an invariant collation that sorts å/ä/ö in among a/o instead of after z - no
    // exception, just wrong-looking template lists. This pins the correct sv-se order so a future
    // base image change or an accidental InvariantGlobalization flag fails a test instead of only
    // showing up as "the templates look wrong" during fas 11's manual pass.
    [TestClass]
    public class TemplateServiceBaseTest
    {
        [TestMethod]
        public void GetManualTemplates_SortsDescriptionsUsingSwedishCollation()
        {
            var service = new FakeTemplateService(new[]
            {
                Template("Zebra"),
                Template("Åsa"),
                Template("Apa"),
                Template("Öppettider"),
                Template("Äpple"),
            });

            var sorted = service.GetManualTemplates().Select(t => t.Description).ToList();

            // Swedish collation: a...z, then å, ä, ö - not "å/ä/ö sorted in among a/o" (the
            // invariant-culture failure mode this test guards against).
            CollectionAssert.AreEqual(
                new List<string> { "Apa", "Zebra", "Åsa", "Äpple", "Öppettider" },
                sorted);
        }

        private static Template Template(string description) => new Template { Description = description, Automatic = false };

        class FakeTemplateService : TemplateServiceBase
        {
            private readonly List<Template> _templates;

            public FakeTemplateService(IEnumerable<Template> templates)
            {
                _templates = templates.ToList();
            }

            protected override IEnumerable<Template> LoadAllTemplates() => _templates;
            protected override void SaveTemplate(Template template) { }
        }
    }
}
