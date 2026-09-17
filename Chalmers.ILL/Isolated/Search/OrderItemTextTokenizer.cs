using System.Linq;
using System.Text.RegularExpressions;

namespace Chalmers.ILL.Isolated.Search
{
    // Isolerat läge steg B: mimics enough of Elasticsearch's standard analyzer for this codebase's
    // actual queries to keep working - lowercase, split on runs of non-alphanumeric characters.
    // That's why "status:Beställd", "status:03\:Beställd" and "status:\"05:Levererad\"" all match a
    // document whose Status is "03:Beställd" today, and why this tokenizer has to agree with it
    // exactly (see TODO-remove-dotnet-framework.md, "Bygg den minimala sökersättaren").
    internal static class OrderItemTextTokenizer
    {
        private static readonly Regex NonAlphanumeric = new Regex(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);

        public static string[] Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return System.Array.Empty<string>();

            return NonAlphanumeric.Split(text.ToLowerInvariant())
                .Where(t => t.Length > 0)
                .ToArray();
        }

        // "citerat värde = alla tokens i följd" - a quoted (or otherwise multi-token) value only
        // matches a document field whose own tokens contain that exact sequence contiguously, not
        // just each token somewhere in the field.
        public static bool ContainsContiguous(string[] haystack, string[] needle)
        {
            if (needle.Length == 0)
                return false;
            if (haystack.Length < needle.Length)
                return false;

            for (var start = 0; start <= haystack.Length - needle.Length; start++)
            {
                var allMatch = true;
                for (var i = 0; i < needle.Length; i++)
                {
                    if (haystack[start + i] != needle[i])
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch)
                    return true;
            }
            return false;
        }
    }
}
