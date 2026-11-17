using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UltraPuck.Core
{
    /// <summary>
    /// A small JSON reader for maps and (later) mod data. Objects become Dictionary&lt;string, object&gt;, arrays
    /// List&lt;object&gt;, numbers stay as their text (<see cref="JsonNumber"/>) so they can become exact fixed-point
    /// values, plus string, bool and null.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.Ws();
            var v = p.Value();
            p.Ws();
            if (!p.End) throw p.Error("unexpected text after the end");
            return v;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s ?? string.Empty; }
            public bool End { get { return _i >= _s.Length; } }

            public FormatException Error(string what)
            {
                int line = 1, col = 1;
                for (var k = 0; k < _i && k < _s.Length; k++) { if (_s[k] == '\n') { line++; col = 1; } else col++; }
                return new FormatException("JSON: " + what + " (line " + line + ", column " + col + ")");
            }

            public void Ws()
            {
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _i++; continue; }
                    // comments are allowed, to make hand-written map and mod files friendlier
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (_i < _s.Length && _s[_i] != '\n') _i++; continue; }
                    break;
                }
            }

            public object Value()
            {
                if (End) throw Error("unexpected end");
                var c = _s[_i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == '-' || (c >= '0' && c <= '9')) return Num();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Error("unexpected '" + c + "'");
            }

            private bool Word(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
                _i += w.Length;
                return true;
            }

            private Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                _i++;
                Ws();
                if (!End && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    Ws();
                    if (End || _s[_i] != '"') throw Error("expected a \"name\"");
                    var key = Str();
                    Ws();
                    if (End || _s[_i] != ':') throw Error("expected ':'");
                    _i++;
                    Ws();
                    d[key] = Value();
                    Ws();
                    if (End) throw Error("unexpected end in an object");
                    if (_s[_i] == ',') { _i++; Ws(); if (!End && _s[_i] == '}') { _i++; return d; } continue; } // trailing comma ok
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Error("expected ',' or '}'");
                }
            }

            private List<object> Arr()
            {
                var l = new List<object>();
                _i++;
                Ws();
                if (!End && _s[_i] == ']') { _i++; return l; }
                while (true)
                {
                    Ws();
                    l.Add(Value());
                    Ws();
                    if (End) throw Error("unexpected end in a list");
                    if (_s[_i] == ',') { _i++; Ws(); if (!End && _s[_i] == ']') { _i++; return l; } continue; }
                    if (_s[_i] == ']') { _i++; return l; }
                    throw Error("expected ',' or ']'");
                }
            }

            private string Str()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (End) throw Error("unfinished text");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("unfinished text");
                    var e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private JsonNumber Num()
            {
                var start = _i;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                return new JsonNumber(_s.Substring(start, _i - start));
            }
        }

        // ------------------------------------------------------------------ reading helpers

        public static Dictionary<string, object> Obj(object v, string what)
        {
            var d = v as Dictionary<string, object>;
            if (d == null) throw new FormatException(what + ": expected an object");
            return d;
        }

        public static List<object> Arr(object v, string what)
        {
            var l = v as List<object>;
            if (l == null) throw new FormatException(what + ": expected a list");
            return l;
        }

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        public static Fix Num(object v, string what)
        {
            var n = v as JsonNumber;
            if (n == null) throw new FormatException(what + ": expected a number");
            return n.ToFix();
        }

        public static int Int(object v, string what) { return Num(v, what).FloorToInt(); }

        public static string Str(object v, string fallback) { return v as string ?? fallback; }

        /// <summary>[x, y] as a vector.</summary>
        public static FixVec2 Vec(object v, string what)
        {
            var l = Arr(v, what);
            if (l.Count != 2) throw new FormatException(what + ": expected [x, y]");
            return new FixVec2(Num(l[0], what), Num(l[1], what));
        }
    }

    /// <summary>A JSON number, kept as text until it's needed.</summary>
    public sealed class JsonNumber
    {
        public readonly string Text;
        public JsonNumber(string text) { Text = text; }
        public Fix ToFix() { return Fix.Parse(Text); }
        public override string ToString() { return Text; }
    }
}
