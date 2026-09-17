using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Chalmers.ILL.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Chalmers.ILL.Isolated.Search
{
    // Isolerat läge steg B: one of these backs every field lookup a query makes against an
    // OrderItemModel. Field names in real Elasticsearch documents are camelCase, derived by NEST
    // from the C# property names (see fas 10's note in the TODO) - re-serializing with the same
    // CamelCasePropertyNamesContractResolver Chalmers.ILL.Services.JsonService already uses for the
    // FOLIO integrations gives the exact same names for free ("sierraInfo.record_id" stays
    // record_id because it was already snake_case, "nodeId"/"followUpDate" etc. get camelCased).
    //
    // _exists_/!_exists_ is implemented as "key present and non-null in this re-serialized
    // document" - correct for every document this searcher will ever actually see (freshly seeded
    // test/isolated data always has the current schema, so every property is always present), but
    // not a faithful replica of Elasticsearch's real exists-in-the-index semantics for hypothetical
    // legacy-schema documents. Documented, not fixed - see the TODO's "avgränsning" note.
    internal sealed class OrderItemDocument
    {
        private static readonly JsonSerializerSettings CamelCaseSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        // All string properties on OrderItemModel except the two [JsonIgnore] computed ones (which
        // were never indexed either) - the 2026-09-16 designbeslut for the search box's fältlösa
        // fritextläge ("Alla textfält på OrderItemModel").
        public static readonly string[] DefaultTextFields = typeof(OrderItemModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Select(p => ToCamelCase(p.Name))
            .ToArray();

        private static string ToCamelCase(string name) =>
            name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

        public OrderItemModel Model { get; }
        private readonly JObject _json;
        private readonly ConcurrentDictionary<string, string[]> _tokenCache = new ConcurrentDictionary<string, string[]>();

        public OrderItemDocument(OrderItemModel model)
        {
            Model = model;
            _json = JObject.FromObject(model, JsonSerializer.Create(CamelCaseSettings));
        }

        public string[] GetFieldTokens(string field) =>
            _tokenCache.GetOrAdd(field, f => OrderItemTextTokenizer.Tokenize(_json.SelectToken(f)?.ToString()));

        public bool FieldExists(string field)
        {
            var token = _json.SelectToken(field);
            return token != null && token.Type != JTokenType.Null;
        }

        public DateTime? GetFieldAsDate(string field)
        {
            var token = _json.SelectToken(field);
            if (token == null || token.Type == JTokenType.Null)
                return null;
            try
            {
                return token.Value<DateTime>();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
