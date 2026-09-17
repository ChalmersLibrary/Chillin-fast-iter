using System.Text;

namespace Chalmers.ILL.Isolated.Search
{
    // Isolerat läge steg B, "Bygg den minimala sökersättaren". A small hand-rolled recursive-descent
    // parser for exactly the query grammar the frågekravlista in TODO-remove-dotnet-framework.md
    // inventories - not a general Lucene query_string implementation. Grammar (lowest to highest
    // precedence, matching Lucene's own NOT > AND > OR):
    //
    //   Or      := And (("OR" | <implicit>) And)*      -- ES's default operator is OR; two clauses
    //                                                      with nothing between them are still OR'd
    //   And     := Not ("AND" Not)*
    //   Not     := ("-" | "!" | "NOT") Not | Primary
    //   Primary := "(" Or ")"
    //            | "*"                                  -- match-all, only meaningful at top level
    //            | Field ":" Value
    //            | Value                                -- bare word/phrase, no field (free text)
    //   Value   := "(" Or(field) ")"                     -- only when preceded by "field:"
    //            | "[" Bound "TO" Bound "]"               -- only when preceded by "field:"
    //            | '"' ... '"'
    //            | <word, backslash-escapes any character>
    //
    // Malformed input is handled leniently (falls back to treating unparseable fragments as bare
    // free-text tokens) rather than throwing - a search box is user input, and a bad query should
    // give an empty-ish result, not a 500.
    internal static class OrderItemQueryParser
    {
        public static IOrderItemQueryNode Parse(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return MatchAllNode.Instance;

            var scanner = new Scanner(query);
            var node = ParseOr(scanner, null);
            return node ?? MatchAllNode.Instance;
        }

        private static IOrderItemQueryNode ParseOr(Scanner s, string fieldContext)
        {
            var left = ParseAnd(s, fieldContext);
            while (true)
            {
                s.SkipWhitespace();
                if (s.AtEnd || s.Peek() == ')')
                    break;

                s.TryConsumeKeyword("OR"); // optional - juxtaposition already means OR

                var posBefore = s.Position;
                var right = ParseAnd(s, fieldContext);
                if (right == null || s.Position == posBefore)
                    break; // nothing more to consume - avoid an infinite loop on garbage input

                left = left == null ? right : new OrNode(left, right);
            }
            return left;
        }

        private static IOrderItemQueryNode ParseAnd(Scanner s, string fieldContext)
        {
            var left = ParseNot(s, fieldContext);
            while (true)
            {
                var checkpoint = s.Position;
                s.SkipWhitespace();
                if (!s.TryConsumeKeyword("AND"))
                {
                    s.Position = checkpoint;
                    break;
                }

                var right = ParseNot(s, fieldContext);
                if (right == null)
                    break;
                left = left == null ? right : new AndNode(left, right);
            }
            return left;
        }

        private static IOrderItemQueryNode ParseNot(Scanner s, string fieldContext)
        {
            s.SkipWhitespace();
            if (s.AtEnd)
                return null;

            if (s.Peek() == '-' || s.Peek() == '!')
            {
                s.Advance();
                var inner = ParseNot(s, fieldContext);
                return inner == null ? null : new NotNode(inner);
            }
            if (s.TryConsumeKeyword("NOT"))
            {
                var inner = ParseNot(s, fieldContext);
                return inner == null ? null : new NotNode(inner);
            }
            return ParsePrimary(s, fieldContext);
        }

        private static IOrderItemQueryNode ParsePrimary(Scanner s, string fieldContext)
        {
            s.SkipWhitespace();
            if (s.AtEnd || s.Peek() == ')')
                return null;

            if (s.Peek() == '(')
            {
                s.Advance();
                var inner = ParseOr(s, fieldContext);
                s.SkipWhitespace();
                if (!s.AtEnd && s.Peek() == ')')
                    s.Advance();
                return inner ?? MatchAllNode.Instance;
            }

            if (fieldContext == null && s.Peek() == '*' && s.IsBoundaryAfterNext())
            {
                s.Advance();
                return MatchAllNode.Instance;
            }

            if (fieldContext == null)
            {
                if (s.Peek() == '"')
                    return new FreeTextNode(s.ReadQuoted());

                var word = s.ReadWord();
                if (word.Length == 0)
                {
                    s.Advance(); // stray character (e.g. an unmatched ')') - skip it so we make progress
                    return null;
                }

                if (!s.AtEnd && s.Peek() == ':')
                {
                    s.Advance();
                    return ParseFieldValue(s, word);
                }

                return new FreeTextNode(word);
            }

            // Inside a field:(...) value group - every leaf belongs to fieldContext.
            if (s.Peek() == '"')
                return new FieldValueNode(fieldContext, s.ReadQuoted());

            var value = s.ReadWord();
            if (value.Length == 0)
            {
                s.Advance();
                return null;
            }
            return new FieldValueNode(fieldContext, value);
        }

        private static IOrderItemQueryNode ParseFieldValue(Scanner s, string field)
        {
            s.SkipWhitespace();
            if (s.AtEnd)
                return new FieldValueNode(field, "");

            if (field == "_exists_")
            {
                var target = s.ReadWord();
                return new ExistsNode(target);
            }

            if (s.Peek() == '(')
            {
                s.Advance();
                var inner = ParseOr(s, field);
                s.SkipWhitespace();
                if (!s.AtEnd && s.Peek() == ')')
                    s.Advance();
                return inner ?? MatchAllNode.Instance;
            }

            if (s.Peek() == '[')
            {
                s.Advance();
                s.SkipWhitespace();
                var lower = s.ReadRangeBound();
                s.SkipWhitespace();
                s.TryConsumeKeyword("TO");
                s.SkipWhitespace();
                var upper = s.ReadRangeBound();
                s.SkipWhitespace();
                if (!s.AtEnd && s.Peek() == ']')
                    s.Advance();
                return new RangeNode(field, lower, upper);
            }

            if (s.Peek() == '"')
                return new FieldValueNode(field, s.ReadQuoted());

            var word = s.ReadWord();
            return new FieldValueNode(field, word);
        }

        // Cursor over the raw query string. Deliberately minimal - just enough scanning primitives
        // for the grammar above, not a general tokenizer.
        private sealed class Scanner
        {
            private readonly string _s;
            public int Position;

            public Scanner(string s)
            {
                _s = s;
            }

            public bool AtEnd => Position >= _s.Length;
            public char Peek() => _s[Position];
            public void Advance() => Position++;

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(Peek()))
                    Position++;
            }

            public bool IsBoundaryAfterNext()
            {
                var next = Position + 1;
                return next >= _s.Length || char.IsWhiteSpace(_s[next]) || _s[next] == ')';
            }

            public bool TryConsumeKeyword(string keyword)
            {
                var checkpoint = Position;
                SkipWhitespace();
                if (Position + keyword.Length > _s.Length)
                {
                    Position = checkpoint;
                    return false;
                }
                for (var i = 0; i < keyword.Length; i++)
                {
                    if (char.ToUpperInvariant(_s[Position + i]) != keyword[i])
                    {
                        Position = checkpoint;
                        return false;
                    }
                }
                var afterKeyword = Position + keyword.Length;
                var boundaryOk = afterKeyword >= _s.Length || char.IsWhiteSpace(_s[afterKeyword]) || _s[afterKeyword] == '(' || _s[afterKeyword] == ')';
                if (!boundaryOk)
                {
                    Position = checkpoint;
                    return false;
                }
                Position = afterKeyword;
                return true;
            }

            // Reads a run of non-delimiter characters, unescaping "\x" to "x" as it goes (so
            // "status:01\:Ny" and "Förlorad\?" yield the literal value with the backslash gone -
            // the tokenizer strips the punctuation either way, this is just about not treating the
            // escaped character as a delimiter while scanning).
            public string ReadWord()
            {
                var sb = new StringBuilder();
                while (!AtEnd)
                {
                    var c = Peek();
                    if (c == '\\' && Position + 1 < _s.Length)
                    {
                        sb.Append(_s[Position + 1]);
                        Position += 2;
                        continue;
                    }
                    if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == ':')
                        break;
                    sb.Append(c);
                    Position++;
                }
                return sb.ToString();
            }

            public string ReadQuoted()
            {
                Advance(); // opening quote
                var sb = new StringBuilder();
                while (!AtEnd && Peek() != '"')
                {
                    if (Peek() == '\\' && Position + 1 < _s.Length)
                    {
                        sb.Append(_s[Position + 1]);
                        Position += 2;
                        continue;
                    }
                    sb.Append(Peek());
                    Position++;
                }
                if (!AtEnd)
                    Advance(); // closing quote
                return sb.ToString();
            }

            // A range bound (date, or "*") never contains whitespace - reads up to the next
            // whitespace or the closing ']', whichever comes first. Unlike ReadWord, ':', '.' etc.
            // are not delimiters here since dates are full of them.
            public string ReadRangeBound()
            {
                var sb = new StringBuilder();
                while (!AtEnd && !char.IsWhiteSpace(Peek()) && Peek() != ']')
                {
                    sb.Append(Peek());
                    Position++;
                }
                return sb.ToString();
            }
        }
    }
}
