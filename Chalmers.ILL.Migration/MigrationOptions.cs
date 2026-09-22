namespace Chalmers.ILL.Migration
{
    public class MigrationOptions
    {
        public string ConnectionString { get; set; }
        public string DataPath { get; set; }
        public int SampleSize { get; set; } = 25;
        public string ElasticSearchUrl { get; set; }
        public string ElasticSearchIndex { get; set; }
    }
}
