using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Chalmers.ILL.Tests.Configuration
{
    // Minimal IWebHostEnvironment test double - DefaultChillinConfiguration.DataPath needs
    // ContentRootPath as its final fallback (fas 6, isolerat läge steg A).
    public class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Chalmers.ILL.Tests";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; } = "/tmp/chalmers-ill-tests-contentroot";
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
