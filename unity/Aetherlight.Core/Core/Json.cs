using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aetherlight.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A minimal JSON document model and parser.
    ///
    /// Hand-rolled rather than taking a dependency, on purpose. Unity's
    /// JsonUtility cannot express the shapes content packs use - dictionaries
    /// keyed by element, absent-versus-zero optional numbers, or a tagged union
    /// like a mote effect - and pulling in Newtonsoft would mean the package no
    /// longer works by dropping the folder into a project. This is a few
    /// hundred lines against a fully specified grammar, and it keeps the core
    /// dependency-free on every platform Unity targets.
    ///
    /// Scope: parsing only. Nothing here writes JSON, because the TypeScript
    /// side owns producing the canonical file.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; private set; }

        private bool _bool;
        private double _number;
        private string _string = "";
        private List<JsonValue>? _array;
        private Dictionary<string, JsonValue>? _object;

        public static readonly JsonValue Null = new JsonValue { Kind = JsonKind.Null };

        public static JsonValue Bool(bool value) => new JsonValue { Kind = JsonKind.Bool, _bool = value };
        public static JsonValue Number(double value) => new JsonValue { Kind = JsonKind.Number, _number = value };
        public static JsonValue String(string value) => new JsonValue { Kind = JsonKind.String, _string = value };
        public static JsonValue Array(List<JsonValue> items) => new JsonValue { Kind = JsonKind.Array, _array = items };
        public static JsonValue Object(Dictionary<string, JsonValue> members) => new JsonValue { Kind = JsonKind.Object, _object = members };

        public bool IsNull => Kind == JsonKind.Null;

        public IReadOnlyList<JsonValue> Items =>
            _array ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();

        public IReadOnlyDictionary<string, JsonValue> Members =>
            _object ?? (IReadOnlyDictionary<string, JsonValue>)new Dictionary<string, JsonValue>();

        /// <summary>Member lookup. Returns Null for a missing key, never throws.</summary>
        public JsonValue this[string key] =>
            _object != null && _object.TryGetValue(key, out var value) ? value : Null;

        public bool Has(string key) => _object != null && _object.ContainsKey(key);

        // Accessors deliberately return a default rather than throwing on a
        // missing or mistyped field. A content pack is data, and a loader that
        // throws on the first absent optional field is unusable; the registry's
        // Validate() is where genuine problems get reported.
        public string AsString(string fallback = "") => Kind == JsonKind.String ? _string : fallback;
        public double AsDouble(double fallback = 0) => Kind == JsonKind.Number ? _number : fallback;
        public int AsInt(int fallback = 0) => Kind == JsonKind.Number ? (int)Math.Round(_number, MidpointRounding.AwayFromZero) : fallback;
        public long AsLong(long fallback = 0) => Kind == JsonKind.Number ? (long)Math.Round(_number, MidpointRounding.AwayFromZero) : fallback;
        public bool AsBool(bool fallback = false) => Kind == JsonKind.Bool ? _bool : fallback;

        /// <summary>Optional number: null when the field is absent or not a number.</summary>
        public double? AsNullableDouble() => Kind == JsonKind.Number ? _number : (double?)null;

        public int? AsNullableInt() => Kind == JsonKind.Number ? (int)Math.Round(_number, MidpointRounding.AwayFromZero) : (int?)null;

        public static JsonValue Parse(string text) => new JsonParser(text).ParseDocument();

        public override string ToString() => Kind switch
        {
            JsonKind.Null => "null",
            JsonKind.Bool => _bool ? "true" : "false",
            JsonKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
            JsonKind.String => "\"" + _string + "\"",
            JsonKind.Array => $"[{Items.Count} items]",
            JsonKind.Object => $"{{{Members.Count} members}}",
            _ => "?",
        };
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int position, int line, int column)
            : base($"{message} (line {line}, column {column})")
        {
            Position = position;
            Line = line;
            Column = column;
        }

        public int Position { get; }
        public int Line { get; }
        public int Column { get; }
    }

    internal sealed class JsonParser
    {
        private readonly string _text;
        private int _index;

        internal JsonParser(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        internal JsonValue ParseDocument()
        {
            SkipWhitespace();
            var value = ParseValue();
            SkipWhitespace();
            if (_index < _text.Length) throw Error("Trailing content after JSON document");
            return value;
        }

        private JsonValue ParseValue()
        {
            SkipWhitespace();
            if (_index >= _text.Length) throw Error("Unexpected end of input");

            char c = _text[_index];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return JsonValue.String(ParseString());
                case 't': Expect("true"); return JsonValue.Bool(true);
                case 'f': Expect("false"); return JsonValue.Bool(false);
                case 'n': Expect("null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return JsonValue.Number(ParseNumber());
                    throw Error($"Unexpected character '{c}'");
            }
        }

        private JsonValue ParseObject()
        {
            _index++; // '{'
            var members = new Dictionary<string, JsonValue>();
            SkipWhitespace();

            if (Peek() == '}') { _index++; return JsonValue.Object(members); }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"') throw Error("Expected a property name");
                string key = ParseString();

                SkipWhitespace();
                if (Peek() != ':') throw Error("Expected ':' after property name");
                _index++;

                members[key] = ParseValue();

                SkipWhitespace();
                char next = Peek();
                if (next == ',') { _index++; continue; }
                if (next == '}') { _index++; return JsonValue.Object(members); }
                throw Error("Expected ',' or '}' in object");
            }
        }

        private JsonValue ParseArray()
        {
            _index++; // '['
            var items = new List<JsonValue>();
            SkipWhitespace();

            if (Peek() == ']') { _index++; return JsonValue.Array(items); }

            while (true)
            {
                items.Add(ParseValue());
                SkipWhitespace();
                char next = Peek();
                if (next == ',') { _index++; continue; }
                if (next == ']') { _index++; return JsonValue.Array(items); }
                throw Error("Expected ',' or ']' in array");
            }
        }

        private string ParseString()
        {
            _index++; // opening quote
            var builder = new StringBuilder();

            while (true)
            {
                if (_index >= _text.Length) throw Error("Unterminated string");
                char c = _text[_index++];

                if (c == '"') return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (_index >= _text.Length) throw Error("Unterminated escape sequence");
                char escape = _text[_index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_index + 4 > _text.Length) throw Error("Truncated \\u escape");
                        // Surrogate pairs need no special handling: each half is
                        // a valid UTF-16 code unit and appending both in order
                        // reconstructs the character.
                        builder.Append((char)Convert.ToUInt16(_text.Substring(_index, 4), 16));
                        _index += 4;
                        break;
                    default: throw Error($"Unknown escape '\\{escape}'");
                }
            }
        }

        private double ParseNumber()
        {
            int start = _index;
            if (Peek() == '-') _index++;

            while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9') _index++;

            if (_index < _text.Length && _text[_index] == '.')
            {
                _index++;
                while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9') _index++;
            }

            if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                _index++;
                if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;
                while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9') _index++;
            }

            string slice = _text.Substring(start, _index - start);
            // InvariantCulture matters: a machine with a comma decimal separator
            // would otherwise silently misparse every number in the file.
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error($"Malformed number '{slice}'");
            return value;
        }

        private char Peek() => _index < _text.Length ? _text[_index] : '\0';

        private void Expect(string literal)
        {
            if (_index + literal.Length > _text.Length || string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                throw Error($"Expected '{literal}'");
            _index += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length)
            {
                char c = _text[_index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _index++;
                else break;
            }
        }

        private JsonParseException Error(string message)
        {
            int line = 1, column = 1;
            for (int i = 0; i < _index && i < _text.Length; i++)
            {
                if (_text[i] == '\n') { line++; column = 1; }
                else column++;
            }
            return new JsonParseException(message, _index, line, column);
        }
    }
}
