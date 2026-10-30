using PartEdit;
using Xunit;

namespace Part3dAsCode.Tests;

public class PartEditTests
{
    static readonly string Example = TestMeshes.ExamplePartText();

    [Fact]
    public void Locate_findsAParamLiteral()
    {
        var at = Literals.Locate(Example, "bracket.w");
        Assert.Equal(LocateKind.Found, at.Kind);
        Assert.Equal("40.0", Example.Substring(at.Start, at.Length));
    }

    [Fact]
    public void Locate_findsAFitLiteral()
    {
        var at = Literals.Locate(Example, "slip");
        Assert.Equal(LocateKind.Found, at.Kind);
        Assert.Equal("0.20", Example.Substring(at.Start, at.Length));
    }

    [Fact]
    public void Locate_findsAnExplicitParamCreation()
    {
        var at = Literals.Locate(Example, "pin.d");
        Assert.Equal(LocateKind.Found, at.Kind);
        Assert.Equal("6.0", Example.Substring(at.Start, at.Length));
    }

    [Fact]
    public void Locate_aDerivedExpressionIsNotALiteral() =>
        Assert.Equal(LocateKind.NotLiteral, Literals.Locate(Example, "hole.d").Kind);

    [Fact]
    public void Locate_anUnknownNameIsMissing() =>
        Assert.Equal(LocateKind.Missing, Literals.Locate(Example, "no.such").Kind);

    [Fact]
    public void Locate_aNameSharedByAParamAndAFitIsAmbiguous()
    {
        const string src = "var a = new Param(\"x\", 1.0);\nvar b = new Fit(\"x\", 0.2);";
        var at = Literals.Locate(src, "x");
        Assert.Equal(LocateKind.Ambiguous, at.Kind);
        Assert.Equal(2, at.Count);
    }

    [Fact]
    public void Locate_ignoresABodyOrAnnotationSharingTheName()
    {
        const string src = "Param p = new(\"x\", 1.0);\nAnnotation a = new(\"x\", \"box\", (0, 0, 0));\nBody b = new(\"x\", \"parts/x.py\");";
        Assert.Equal(LocateKind.Found, Literals.Locate(src, "x").Kind);
    }

    [Fact]
    public void Locate_ignoresNamesInCommentsAndOtherStrings()
    {
        const string src = "// new(\"x\", 1.0)\nvar s = \"new(\\\"x\\\", 1.0)\";\nvar n = Foo(\"x\", 2.0);";
        Assert.Equal(LocateKind.Missing, Literals.Locate(src, "x").Kind);
    }

    [Fact]
    public void Locate_aNegativeLiteralSpansItsSign()
    {
        const string src = "Param p = new(\"x\", -2.0, Tol: 0.1);";
        var at = Literals.Locate(src, "x");
        Assert.Equal(LocateKind.Found, at.Kind);
        Assert.Equal("-2.0", src.Substring(at.Start, at.Length));
    }

    [Fact]
    public void SetValue_changesOnlyTheLiteral()
    {
        var r = Literals.SetValue(Example, "bracket.w", 42);
        Assert.True(r.Ok);
        Assert.Equal(Example.Replace("new(\"bracket.w\", 40.0,", "new(\"bracket.w\", 42.0,"), r.Text);
    }

    [Fact]
    public void SetValue_keepsCrlfLineEndings()
    {
        var crlf = Example.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var r = Literals.SetValue(crlf, "base.t", 4.5);
        Assert.True(r.Ok);
        Assert.Equal(crlf.Replace("new(\"base.t\", 4.0,", "new(\"base.t\", 4.5,"), r.Text);
        Assert.DoesNotContain("\n", r.Text.Replace("\r\n", ""));
    }

    [Theory]
    [InlineData(-2.0, 3.0, "3.0")]
    [InlineData(2.0, -3.0, "-3.0")]
    [InlineData(-2.0, -3.5, "-3.5")]
    public void SetValue_getsTheSignRight(double from, double to, string text)
    {
        var src = $"Param p = new(\"x\", {Literals.Format(from)}, Tol: 0.1);";
        var r = Literals.SetValue(src, "x", to);
        Assert.Equal($"Param p = new(\"x\", {text}, Tol: 0.1);", r.Text);
    }

    [Theory]
    [InlineData(41, "41.0")]
    [InlineData(0.1, "0.1")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(1e-7, "0.0000001")]
    [InlineData(1e21, "1000000000000000000000.0")]
    [InlineData(1.5e-25, "0.00000000000000000000000015")]
    [InlineData(-2.5e-10, "-0.00000000025")]
    [InlineData(1.2345678901234566e-7, "0.00000012345678901234566")]
    [InlineData(-0.0, "0.0")]
    public void Format_isInvariantRoundTripAndHasADecimal(double v, string expected)
    {
        Assert.Equal(expected, Literals.Format(v));
        Assert.Equal(v, double.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Format_ignoresTheCurrentCulture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
            Assert.Equal("6.2", Literals.Format(6.2));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void SetValue_reportsInsteadOfThrowing()
    {
        Assert.Contains("not a numeric literal", Literals.SetValue(Example, "hole.d", 7).Problem);
        Assert.Contains("no declaration", Literals.SetValue(Example, "no.such", 7).Problem);
        Assert.Contains("not a finite", Literals.SetValue(Example, "bracket.w", double.NaN).Problem);
        Assert.Equal(Example, Literals.SetValue(Example, "hole.d", 7).Text);
    }

    [Fact]
    public void SetArgument_appendsAMissingNamedArgument()
    {
        const string src = "Param p = new(\"x\", 1.0, Tol: 0.1);\nParam q = new(\"y\", 2.0);";
        var r = Literals.SetArgument(src, "x", "Source", "Source.Measured");
        Assert.Equal("Param p = new(\"x\", 1.0, Tol: 0.1, Source: Source.Measured);\nParam q = new(\"y\", 2.0);", r.Text);
        r = Literals.SetArgument(src, "y", "Ref", Literals.Quote("D3: 6.2"));
        Assert.EndsWith("new(\"y\", 2.0, Ref: \"D3: 6.2\");", r.Text);
    }

    [Fact]
    public void SetArgument_replacesAPositionalArgumentAtTheGivenPosition()
    {
        const string src = "Param p = new(\"x\", 1.0, \"mm\", Source.Chosen, 0.1, false, \"old\");";
        var r = Literals.SetArgument(src, "x", "Source", "Source.Measured", position: 3);
        r = Literals.SetArgument(r.Text, "x", "Ref", Literals.Quote("D3: 6.2"), position: 6);
        Assert.Equal("Param p = new(\"x\", 1.0, \"mm\", Source.Measured, 0.1, false, \"D3: 6.2\");", r.Text);
    }

    [Fact]
    public void SetArgument_appendsWhenThePositionIsNotGiven()
    {
        const string src = "Param p = new(\"x\", 1.0, \"mm\");";
        var r = Literals.SetArgument(src, "x", "Source", "Source.Measured", position: 3);
        Assert.Equal("Param p = new(\"x\", 1.0, \"mm\", Source: Source.Measured);", r.Text);
    }

    [Fact]
    public void SetArgument_replacesAnExistingNamedArgument()
    {
        const string src = "Param p = new(\"x\", 1.0, Source: Source.Chosen, Tol: 0.1);";
        var r = Literals.SetArgument(src, "x", "Source", "Source.Measured");
        Assert.Equal("Param p = new(\"x\", 1.0, Source: Source.Measured, Tol: 0.1);", r.Text);
    }

    [Fact]
    public void Quote_escapesQuotesAndBackslashes()
    {
        var q = Literals.Quote("a \"b\" \\ c");
        Assert.Equal("\"a \\\"b\\\" \\\\ c\"", q);
        var src = $"Param p = new(\"x\", 1.0, Ref: \"old\");";
        var r = Literals.SetArgument(src, "x", "Ref", q);
        Assert.Equal(LocateKind.Found, Literals.Locate(r.Text, "x").Kind);
        Assert.Contains(q, r.Text);
    }

    [Fact]
    public void SetArgument_onAnUneditableNameReports()
    {
        Assert.Contains("not a numeric literal", Literals.SetArgument(Example, "hole.d", "Ref", "\"x\"").Problem);
    }
}
