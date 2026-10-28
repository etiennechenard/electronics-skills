using Sx;
using Xunit;

namespace PcbAsCode.Tests;

public class SexprTests
{
    [Fact]
    public void Parse_readsAtomsQuotedAndNested()
    {
        var t = (List<object>)Sexpr.Parse("(kicad_pcb (version 20250114) (generator \"pcb new\") 1.27)");
        Assert.Equal("kicad_pcb", ((Sym)t[0]).Value);
        var version = Sexpr.Find(t, "version");
        Assert.NotNull(version);
        Assert.Equal("20250114", version![1].Txt());
        var gen = Sexpr.Find(t, "generator");
        Assert.Equal("pcb new", gen![1].Txt());          // quoted stays quoted
        Assert.IsType<Sym>(t[^1]);                        // 1.27 stays a bare atom
        Assert.Equal("1.27", ((Sym)t[^1]).Value);
    }

    [Theory]
    [InlineData("(kicad_pcb (version 1) (footprint \"R\" (at 1 2)")]     // cut off inside an item
    [InlineData("(kicad_pcb (version 1)")]                                  // cut off between items
    [InlineData("(a) b)")]
    public void Parse_throwsOnUnbalancedParentheses(string text) =>
        Assert.Throws<InvalidOperationException>(() => Sexpr.Parse(text));

    [Fact]
    public void Parse_unescapesQuotedStrings()
    {
        var t = (List<object>)Sexpr.Parse("(s \"a\\n\\tb\\\"c\\\\d\" bare)");
        Assert.Equal("a\n\tb\"c\\d", t[1]);
        Assert.IsType<Sym>(t[2]);
    }

    [Fact]
    public void Parse_throwsPointedError_onDsnStyleUnterminatedQuote()
    {
        // pcbnew's DSN carries `(string_quote ")` — the parser must refuse it
        // with the pointed message, not loop or crash (dotnet-conventions.md).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Sexpr.Parse("(parser\n  (string_quote \")"));
        Assert.Contains("not plain s-expressions", ex.Message);
    }

    [Fact]
    public void Parse_throwsNotIndexOutOfRange_onTrailingBackslashInQuote()
    {
        // text ending on a backslash escape: must be the unterminated-string
        // error, not an IndexOutOfRangeException
        Assert.Throws<InvalidOperationException>(() => Sexpr.Parse("(s \"x\\"));
    }

    [Fact]
    public void Parse_singleRootYieldsTheRoot_itself()
    {
        var one = Sexpr.Parse("(a b)");
        Assert.IsType<List<object>>(one);
        var twoRoots = (List<object>)Sexpr.Parse("(a)(b)");
        Assert.Equal(2, twoRoots.Count);
        Assert.All(twoRoots, e => Assert.IsType<List<object>>(e));
    }

    [Fact]
    public void Atom_formatsNumbersBoolsAndStrings()
    {
        Assert.Equal("yes", Sexpr.Atom(true));
        Assert.Equal("no", Sexpr.Atom(false));
        Assert.Equal("42", Sexpr.Atom(42L));
        Assert.Equal("1.27", Sexpr.Atom(1.27));
        Assert.Equal("2", Sexpr.Atom(2.0));              // trailing zeros stripped
        Assert.Equal("0", Sexpr.Atom(-0.000001));        // -0.0000 -> -0 -> 0
        Assert.Equal("1.2346", Sexpr.Atom(1.23456));     // 4 decimals, rounded
        Assert.Equal("\"a\\\"b\\nc\"", Sexpr.Atom("a\"b\nc"));
        Assert.Equal("bare", Sexpr.Atom(new Sym("bare")));
    }

    [Fact]
    public void Dump_inlinesShortLists()
    {
        var node = Sexpr.S("version", 20250114);
        Assert.Equal("(version 20250114)", Sexpr.Dump(node));
    }

    [Fact]
    public void Dump_breaksLongListsAtFirstSubList()
    {
        var node = Sexpr.S("kicad_pcb", Sexpr.S("very", "long", new string('x', 120)), "tail");
        var d = Sexpr.Dump(node);
        Assert.StartsWith("(kicad_pcb\n", d);                     // head atoms on line 1, LF
        Assert.EndsWith(")", d);                                  // no trailing newline
        Assert.Contains("  (very", d);                              // child indented
        Assert.Contains("\n)", d);                                  // closing at parent indent
    }

    [Fact]
    public void Dump_isRoundTripStable()
    {
        var node = Sexpr.S("comp",
            Sexpr.S("ref", "R1"),
            Sexpr.S("value", "a\"b\\c\nd"),
            Sexpr.S("at", 12.7, -25.4, 90),
            Sexpr.S("dnp", true),
            Sexpr.S("children", Sexpr.S("x"), Sexpr.S("y", 1), Sexpr.S("z", 2), Sexpr.S("w", 3)),
            new string('k', 150));
        var once = Sexpr.Dump(node);
        var back = (List<object>)Sexpr.Parse(once);
        Assert.Equal(once, Sexpr.Dump(back));            // re-dump is byte-identical
    }

    [Fact]
    public void Dump_roundTripsThroughParse()
    {
        var node = Sexpr.S("zone", Sexpr.S("net_name", "GND"), Sexpr.S("layer", "F.Cu"),
            Sexpr.S("polygon", Sexpr.S("pts", Sexpr.S("xy", 1.0, 2.0), Sexpr.S("xy", 3.5, 4.25))));
        var parsed = (List<object>)Sexpr.Parse(Sexpr.Dump(node));
        Assert.Equal("GND", Sexpr.Find(parsed, "net_name")![1].Txt());
        Assert.Equal("F.Cu", Sexpr.Find(parsed, "layer")![1].Txt());
        var pts = Sexpr.Find(Sexpr.Find(parsed, "polygon")!, "pts")!;
        Assert.Equal(2, pts.Count(e => e is List<object>));
        Assert.Equal("4.25", Sexpr.FindAll(pts, "xy")[1][2].Txt());
    }

    [Fact]
    public void FindAndFindAll_matchSymAndStringHeads()
    {
        var t = (List<object>)Sexpr.Parse("(n (sym-headed 1) \"plain\" (sym-headed 2) (\"string-headed\" 3))");
        Assert.Equal("1", Sexpr.Find(t, "sym-headed")![1].Txt());
        Assert.Equal(2, Sexpr.FindAll(t, "sym-headed").Count);
        Assert.Equal("3", Sexpr.Find(t, "string-headed")![1].Txt());
        Assert.Null(Sexpr.Find(t, "missing"));
    }

    [Fact]
    public void S_makesFirstItemASym_othersUntouched()
    {
        var node = Sexpr.S("prop", "Reference", 1.5, true);
        Assert.IsType<Sym>(node[0]);
        Assert.IsType<string>(node[1]);                  // stays quoted
        Assert.IsType<double>(node[2]);
        Assert.IsType<bool>(node[3]);
    }

    [Fact]
    public void ValueGetters_convertBareAtoms()
    {
        var t = (List<object>)Sexpr.Parse("(at 1.5 -3 1000000)");
        Assert.Equal(1.5, t[1].D());
        Assert.Equal(-3L, t[2].L());
        Assert.Equal("1.5", t[1].Txt());
    }
}
