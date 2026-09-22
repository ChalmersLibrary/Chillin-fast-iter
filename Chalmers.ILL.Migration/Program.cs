using System;
using System.Linq;
using Chalmers.ILL.OrderItems;
using Elasticsearch.Net;
using Nest;

namespace Chalmers.ILL.Migration
{
    // CLI entry point for fas 7's "Bygg engångsmigreringen från SQL till filer" - see
    // TODO-remove-dotnet-framework.md. Usage:
    //
    //   dotnet run --project Chalmers.ILL.Migration -- \
    //     --connection-string "<restored copy, NEVER the live database>" \
    //     --data-path /path/to/chillin-data \
    //     [--sample-size 25] \
    //     [--elasticsearch-url http://localhost:9200 --elasticsearch-index chillin]
    //
    // --connection-string must point at a locally restored copy of an exported dump (e.g. a
    // bacpac imported into a throwaway SQL Server instance) - this tool never talks to the live
    // database directly.
    public static class Program
    {
        public static int Main(string[] args)
        {
            var options = ParseArgs(args);
            if (options == null)
            {
                PrintUsage();
                return 1;
            }

            var source = new SqlOrderItemSource(options.ConnectionString);
            var writer = new OrderFileWriter(System.IO.Path.Combine(options.DataPath, "orders"));

            IOrderItemSearcher searcher = null;
            if (!string.IsNullOrEmpty(options.ElasticSearchUrl))
            {
                var settings = new ConnectionSettings(new Uri(options.ElasticSearchUrl))
                    .DefaultIndex(options.ElasticSearchIndex);
                searcher = new ElasticSearchOrderItemSearcher(new ElasticClient(settings));
            }

            var runner = new MigrationRunner(source, writer, options.SampleSize, searcher);
            var result = runner.Run();

            PrintReport(result);
            return result.Success ? 0 : 1;
        }

        private static void PrintReport(MigrationResult result)
        {
            Console.WriteLine();
            Console.WriteLine("=== Migreringsrapport ===");
            Console.WriteLine($"Lästa ordrar (källa):        {result.SourceOrderCount}");
            Console.WriteLine($"Skrivna filer:               {result.WrittenFileCount}");
            Console.WriteLine($"Loggposter (källa):          {result.SourceLogItemCount}");
            Console.WriteLine($"Bilagor (källa):             {result.SourceAttachmentCount}");
            Console.WriteLine($"Högsta NodeId:               {result.MaxNodeId}");
            Console.WriteLine($"Stickprovskontrollerade:     {string.Join(", ", result.SpotCheckedNodeIds)}");
            if (result.ElasticSearchDocCount.HasValue)
                Console.WriteLine($"Elasticsearch-dokument:      {result.ElasticSearchDocCount}");

            if (result.Success)
            {
                Console.WriteLine();
                Console.WriteLine("OK - alla kontroller gick igenom.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("MISSLYCKADES - följande kontroller slog fel:");
                foreach (var error in result.Errors)
                    Console.WriteLine(" - " + error);
            }
        }

        private static MigrationOptions ParseArgs(string[] args)
        {
            var options = new MigrationOptions();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--connection-string":
                        options.ConnectionString = args[++i];
                        break;
                    case "--data-path":
                        options.DataPath = args[++i];
                        break;
                    case "--sample-size":
                        options.SampleSize = int.Parse(args[++i]);
                        break;
                    case "--elasticsearch-url":
                        options.ElasticSearchUrl = args[++i];
                        break;
                    case "--elasticsearch-index":
                        options.ElasticSearchIndex = args[++i];
                        break;
                    default:
                        Console.Error.WriteLine("Okänt argument: " + args[i]);
                        return null;
                }
            }

            if (string.IsNullOrEmpty(options.ConnectionString) || string.IsNullOrEmpty(options.DataPath))
                return null;

            return options;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Användning: --connection-string <conn> --data-path <path> [--sample-size N] [--elasticsearch-url url --elasticsearch-index namn]");
            Console.WriteLine("VIKTIGT: --connection-string ska peka på en lokalt återställd kopia (t.ex. från en bacpac), aldrig direkt på den skarpa databasen.");
        }
    }
}
