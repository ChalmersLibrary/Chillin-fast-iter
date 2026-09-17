using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chalmers.ILL.Isolated.Search
{
    // Isolerat läge steg B, "Bygg den minimala sökersättaren" - the AST the parser
    // (OrderItemQueryParser) produces, and the evaluation against one document's camelCase JObject
    // (see OrderItemDocument). Matching is deliberately field-name/token based, not a real
    // relevance-scoring engine - see TODO-remove-dotnet-framework.md for the documented divergences
    // from real Elasticsearch behaviour (this is the isolated test server's searcher, not a
    // production search engine).
    internal interface IOrderItemQueryNode
    {
        bool Matches(OrderItemDocument doc);
    }

    internal sealed class MatchAllNode : IOrderItemQueryNode
    {
        public static readonly MatchAllNode Instance = new MatchAllNode();
        public bool Matches(OrderItemDocument doc) => true;
    }

    internal sealed class NotNode : IOrderItemQueryNode
    {
        private readonly IOrderItemQueryNode _inner;
        public NotNode(IOrderItemQueryNode inner) => _inner = inner;
        public bool Matches(OrderItemDocument doc) => !_inner.Matches(doc);
    }

    internal sealed class AndNode : IOrderItemQueryNode
    {
        private readonly IOrderItemQueryNode _left, _right;
        public AndNode(IOrderItemQueryNode left, IOrderItemQueryNode right) { _left = left; _right = right; }
        public bool Matches(OrderItemDocument doc) => _left.Matches(doc) && _right.Matches(doc);
    }

    internal sealed class OrNode : IOrderItemQueryNode
    {
        private readonly IOrderItemQueryNode _left, _right;
        public OrNode(IOrderItemQueryNode left, IOrderItemQueryNode right) { _left = left; _right = right; }
        public bool Matches(OrderItemDocument doc) => _left.Matches(doc) || _right.Matches(doc);
    }

    // field:value, field:"phrase" and field:v1\:Ny all funnel through here - see
    // OrderItemTextTokenizer for why quoting/escaping doesn't matter once tokenized.
    internal sealed class FieldValueNode : IOrderItemQueryNode
    {
        private readonly string _field;
        private readonly string[] _valueTokens;

        public FieldValueNode(string field, string value)
        {
            _field = field;
            _valueTokens = OrderItemTextTokenizer.Tokenize(value);
        }

        public bool Matches(OrderItemDocument doc)
        {
            if (_valueTokens.Length == 0)
                return false;

            var fieldTokens = doc.GetFieldTokens(_field);
            return OrderItemTextTokenizer.ContainsContiguous(fieldTokens, _valueTokens);
        }
    }

    // A bare word or bare quoted phrase with no field: prefix - the "enda genuint fria ytan"
    // (search box free text). Matched against every default text field (all string properties on
    // OrderItemModel per the 2026-09-16 designbeslut), OR'd together.
    internal sealed class FreeTextNode : IOrderItemQueryNode
    {
        private readonly string[] _valueTokens;

        public FreeTextNode(string value)
        {
            _valueTokens = OrderItemTextTokenizer.Tokenize(value);
        }

        public bool Matches(OrderItemDocument doc)
        {
            if (_valueTokens.Length == 0)
                return false;

            foreach (var field in OrderItemDocument.DefaultTextFields)
            {
                if (OrderItemTextTokenizer.ContainsContiguous(doc.GetFieldTokens(field), _valueTokens))
                    return true;
            }
            return false;
        }
    }

    internal sealed class ExistsNode : IOrderItemQueryNode
    {
        private readonly string _field;
        public ExistsNode(string field) => _field = field;

        public bool Matches(OrderItemDocument doc) => doc.FieldExists(_field);
    }

    // field:[lower TO upper], either bound may be "*" (open). Only ever used on DateTime-typed
    // fields in the actual call sites (followUpDate/updateDate/dueDate/deliveryDate) - so bounds
    // and the field's value are both parsed as dates, not token-matched. Accepts both the
    // "yyyy-MM-ddTHH:mm:ss.fffZ" form used everywhere in C#, the plain "yyyy-MM-dd" form, and the
    // compact "yyyyMMdd" form the statistics page's JS builds - permissive on purpose, since this
    // isn't replicating ES's own date-format configuration, just parsing whatever this codebase's
    // callers actually send it.
    internal sealed class RangeNode : IOrderItemQueryNode
    {
        private readonly string _field;
        private readonly DateTime? _lower;
        private readonly DateTime? _upper;

        public RangeNode(string field, string lowerText, string upperText)
        {
            _field = field;
            _lower = ParseBound(lowerText);
            _upper = ParseBound(upperText);
        }

        private static DateTime? ParseBound(string text)
        {
            if (string.IsNullOrEmpty(text) || text == "*")
                return null;

            string[] formats =
            {
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-dd",
                "yyyyMMdd"
            };
            if (DateTime.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var exact))
                return exact;
            if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var loose))
                return loose;
            return null;
        }

        public bool Matches(OrderItemDocument doc)
        {
            var value = doc.GetFieldAsDate(_field);
            if (value == null)
                return false;

            if (_lower.HasValue && value.Value < _lower.Value)
                return false;
            if (_upper.HasValue && value.Value > _upper.Value)
                return false;
            return true;
        }
    }
}
