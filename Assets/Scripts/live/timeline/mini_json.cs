using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

// minimal json parser/writer (calculated off the public-domain MiniJson pattern).
namespace UV2.Live
{
    public static class MiniJson
    {
        // parses json text into dictionaries, lists, longs, doubles, strings, bools, null.
        public static object Parse(string text)
        {
            var parser = new parser(text);
            return parser.parse_value();
        }

        private class parser
        {
            private readonly string json;
            private int at;

            public parser(string source)
            {
                json = source;
                at = 0;
            }

            public object parse_value()
            {
                skip_whitespace();
                if (at >= json.Length) throw new FormatException("unexpected end of json");
                char c = json[at];
                return c switch
                {
                    '{' => parse_object(),
                    '[' => parse_array(),
                    '"' => parse_string(),
                    't' or 'f' => parse_bool(),
                    'n' => parse_null(),
                    _ => parse_number(),
                };
            }

            private object parse_object()
            {
                var result = new Dictionary<string, object>();
                at++; // {
                skip_whitespace();
                if (at < json.Length && json[at] == '}')
                {
                    at++;
                    return result;
                }
                while (true)
                {
                    skip_whitespace();
                    string key = parse_string();
                    skip_whitespace();
                    if (json[at] != ':') throw new FormatException("expected : at " + at);
                    at++;
                    result[key] = parse_value();
                    skip_whitespace();
                    if (at >= json.Length) throw new FormatException("unterminated object");
                    if (json[at] == ',') { at++; continue; }
                    if (json[at] == '}') { at++; return result; }
                    throw new FormatException("expected , or } at " + at);
                }
            }

            private object parse_array()
            {
                var result = new List<object>();
                at++; // [
                skip_whitespace();
                if (at < json.Length && json[at] == ']')
                {
                    at++;
                    return result;
                }
                while (true)
                {
                    result.Add(parse_value());
                    skip_whitespace();
                    if (at >= json.Length) throw new FormatException("unterminated array");
                    if (json[at] == ',') { at++; continue; }
                    if (json[at] == ']') { at++; return result; }
                    throw new FormatException("expected , or ] at " + at);
                }
            }

            private string parse_string()
            {
                if (json[at] != '"') throw new FormatException("expected string at " + at);
                at++;
                var sb = new StringBuilder();
                while (at < json.Length)
                {
                    char c = json[at++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        char e = json[at++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                sb.Append((char)Convert.ToInt32(json.Substring(at, 4), 16));
                                at += 4;
                                break;
                            default: throw new FormatException("bad escape \\" + e);
                        }
                    }
                    else sb.Append(c);
                }
                throw new FormatException("unterminated string");
            }

            private object parse_bool()
            {
                if (json[at] == 't') { at += 4; return true; }
                at += 5;
                return false;
            }

            private object parse_null()
            {
                at += 4;
                return null;
            }

            private object parse_number()
            {
                int start = at;
                while (at < json.Length && "+-0123456789.eE".Contains(json[at])) at++;
                string token = json.Substring(start, at - start);
                if (token.Contains('.') || token.Contains('e') || token.Contains('E'))
                    return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
                if (long.TryParse(token, out long l)) return l;
                return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
            }

            private void skip_whitespace()
            {
                while (at < json.Length && " \t\n\r".Contains(json[at])) at++;
            }
        }
    }
}
