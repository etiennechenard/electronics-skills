// Minimal S-expression reader/writer for KiCad files (schematics, symbol
// libraries, footprints, Specctra SES) and builder helpers. Conventions:
//
//   - bare atoms are Sym, quoted tokens are string; parse never makes numbers
//     (convert with .D() / .L() when you read one), builders may hold long,
//     double or bool (dumped bare / bare / yes-no)
//   - Dump() inlines short lists and breaks long ones at their first
//     sub-list, so output stays diff-friendly and KiCad-parseable
//
// Library file for #:include — types only, no top-level statements.

using System.Globalization;
using System.Text;

namespace Sx;

/// A bare (unquoted) atom, e.g. <c>yes</c> or <c>1.27</c>. Distinct from a
/// quoted string element, which is a plain C# string.
public sealed class Sym(string value)
{
    public string Value { get; } = value;
    public override string ToString() => Value;
}

public static class Sexpr
{
    /// Parse text into nested lists whose elements are <see cref="Sym"/>,
    /// string, or List&lt;object&gt;. A file with a single root node yields
    /// that root list; several roots yield the list of them. Unbalanced
    /// parentheses (a cut-off file) throw.
    public static object Parse(string text)
    {
        var stack = new Stack<List<object>>();
        var cur = new List<object>();
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (c == '(')
            {
                stack.Push(cur);
                cur = [];
                i++;
            }
            else if (c == ')')
            {
                if (stack.Count == 0)
                    throw new InvalidOperationException($"unbalanced ')' at offset {i}");
                var done = cur;
                cur = stack.Pop();
                cur.Add(done);
                i++;
            }
            else if (c == '"')
            {
                int j = i + 1;
                var buf = new StringBuilder();
                while (j >= n || text[j] != '"')
                {
                    if (j >= n)
                        throw new InvalidOperationException(
                            "unterminated quoted string (KiCad DSN files are not " +
                            "plain s-expressions: pcbnew writes (string_quote \") — " +
                            "handle DSN text with regexes, not this parser)");
                    if (text[j] == '\\')
                    {
                        j++;
                        if (j >= n)
                            throw new InvalidOperationException(
                                "unterminated quoted string (KiCad DSN files are not " +
                                "plain s-expressions: pcbnew writes (string_quote \") — " +
                                "handle DSN text with regexes, not this parser)");
                        buf.Append(text[j] == 'n' ? '\n' : text[j] == 't' ? '\t' : text[j]);
                    }
                    else
                        buf.Append(text[j]);
                    j++;
                }
                cur.Add(buf.ToString());
                i = j + 1;
            }
            else if (char.IsWhiteSpace(c))
                i++;
            else
            {
                int j = i;
                while (j < n && !char.IsWhiteSpace(text[j]) && text[j] != '(' && text[j] != ')' && text[j] != '"')
                    j++;
                cur.Add(new Sym(text[i..j]));
                i = j;
            }
        }
        if (stack.Count > 0)
            throw new InvalidOperationException($"unbalanced: {stack.Count} list(s) not closed (a cut-off file?)");
        return cur.Count == 1 ? cur[0] : cur;
    }

    /// First child list whose head is <paramref name="key"/> (head compared
    /// by value, Sym or quoted string alike), or null.
    public static List<object>? Find(List<object> lst, string key)
    {
        foreach (var e in lst)
            if (e is List<object> l && l.Count > 0 && HeadIs(l[0], key))
                return l;
        return null;
    }

    public static List<List<object>> FindAll(List<object> lst, string key) =>
        lst.Where(e => e is List<object> l && l.Count > 0 && HeadIs(l[0], key)).Cast<List<object>>().ToList();

    /// Build a list node; the first item (the keyword) becomes a Sym, every
    /// other item is placed as given (string stays quoted, long/double/bool
    /// dump bare).
    public static List<object> S(params object[] items)
    {
        var l = new List<object>(items.Length);
        for (int k = 0; k < items.Length; k++)
            l.Add(k == 0 && items[0] is string s ? new Sym(s) : items[k]);
        return l;
    }

    public static string Atom(object v) => v switch
    {
        Sym s => s.Value,
        bool b => b ? "yes" : "no",
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => FmtNum(d),
        _ => Quote(v.ToString() ?? "")
    };

    private static string FmtNum(double v)
    {
        var s = v.ToString("F4", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return s is "-0" or "" ? "0" : s;
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    private static string Inline(object x) =>
        x is List<object> l ? "(" + string.Join(" ", l.Select(Inline)) + ")" : Atom(x);

    public static string Dump(object x, int indent = 0)
    {
        if (x is not List<object> list)
            return Atom(x);
        var flat = Inline(list);
        if (flat.Length + 2 * indent <= 100)
            return flat;
        var pad = new string(' ', 2 * (indent + 1));
        int split = 0;
        while (split < list.Count && list[split] is not List<object>)
            split++;
        var head = string.Join(" ", list.Take(split).Select(Atom));
        var outs = "(" + head;
        foreach (var e in list.Skip(split))
            outs += "\n" + pad + Dump(e, indent + 1);
        return outs + "\n" + new string(' ', 2 * indent) + ")";
    }

    private static bool HeadIs(object head, string key) =>
        head is Sym s ? s.Value == key : head is string str && str == key;
}

public static class SexprExt
{
    /// Value of a bare atom as double (parse never makes numbers).
    public static double D(this object o) => double.Parse(((Sym)o).Value, CultureInfo.InvariantCulture);

    /// Value of a bare atom as long (KiCad PCB coordinates are nm integers).
    public static long L(this object o) => long.Parse(((Sym)o).Value, CultureInfo.InvariantCulture);

    /// Value of an atom of either kind as text.
    public static string Txt(this object o) => o switch
    {
        Sym s => s.Value,
        string s => s,
        _ => throw new InvalidOperationException($"not an atom: {o}")
    };
}
