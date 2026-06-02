using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MusicBeePlugin
{
    internal static class SimpleJson
    {
        public static object Parse(string json)
        {
            return new Parser(json).ParseValue();
        }

        public static string Stringify(object value)
        {
            StringBuilder builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        public static string GetString(IDictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        public static bool GetBool(IDictionary<string, object> obj, string key, bool fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
            }
            else if (value is string)
            {
                WriteString(builder, (string)value);
            }
            else if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
            }
            else if (value is int || value is long || value is float || value is double || value is decimal)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is IDictionary)
            {
                bool first = true;
                builder.Append('{');
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    first = false;
                    WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    builder.Append(':');
                    WriteValue(builder, entry.Value);
                }
                builder.Append('}');
            }
            else if (value is IEnumerable)
            {
                bool first = true;
                builder.Append('[');
                foreach (object item in (IEnumerable)value)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    first = false;
                    WriteValue(builder, item);
                }
                builder.Append(']');
            }
            else
            {
                WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json ?? "";
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (index >= json.Length)
                {
                    throw new FormatException("Unexpected end of JSON.");
                }

                char c = json[index];
                if (c == '"') return ParseString();
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == 't') return ParseLiteral("true", true);
                if (c == 'f') return ParseLiteral("false", false);
                if (c == 'n') return ParseLiteral("null", null);
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                index++;
                SkipWhitespace();
                if (Peek('}'))
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (Peek('}'))
                    {
                        index++;
                        break;
                    }
                    Expect(',');
                }

                return result;
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                index++;
                SkipWhitespace();
                if (Peek(']'))
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek(']'))
                    {
                        index++;
                        break;
                    }
                    Expect(',');
                }

                return result;
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder builder = new StringBuilder();
                while (index < json.Length)
                {
                    char c = json[index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        throw new FormatException("Invalid JSON string escape.");
                    }

                    char escape = json[index++];
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
                            if (index + 4 > json.Length) throw new FormatException("Invalid unicode escape.");
                            string hex = json.Substring(index, 4);
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default:
                            throw new FormatException("Unknown JSON string escape.");
                    }
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private object ParseNumber()
            {
                int start = index;
                if (Peek('-')) index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
                if (Peek('.'))
                {
                    index++;
                    while (index < json.Length && char.IsDigit(json[index])) index++;
                }
                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                    while (index < json.Length && char.IsDigit(json[index])) index++;
                }

                string text = json.Substring(start, index - start);
                if (text.IndexOf('.') >= 0 || text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
                {
                    double d;
                    if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
                }
                else
                {
                    long l;
                    if (long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out l)) return l;
                }

                throw new FormatException("Invalid JSON number.");
            }

            private object ParseLiteral(string literal, object value)
            {
                if (string.Compare(json, index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                {
                    throw new FormatException("Invalid JSON literal.");
                }
                index += literal.Length;
                return value;
            }

            private void SkipWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            private bool Peek(char c)
            {
                return index < json.Length && json[index] == c;
            }

            private void Expect(char c)
            {
                if (!Peek(c))
                {
                    throw new FormatException("Expected '" + c + "'.");
                }
                index++;
            }
        }
    }
}
