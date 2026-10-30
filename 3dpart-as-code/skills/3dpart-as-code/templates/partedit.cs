// Finds and rewrites single numeric literals in part.cs by its syntax tree,
// never by regex: the parameter sheet's pull writes a changed number back
// into the declaration that holds it and touches nothing else. A value is
// editable only where part.cs declares it as one literal:
//   new("bracket.w", 40.0, ...)   or   new Param("bracket.w", 40.0, ...)
// i.e. an object creation whose first argument is the string literal of the
// name and whose second is a numeric literal (optionally negated).
//
// Failures a user can cause (missing, ambiguous, not a literal) come back
// as results, never as exceptions.
//
// Library file for #:include — types only, no top-level statements.
// Needs the Microsoft.CodeAnalysis.CSharp package.

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PartEdit;

public enum LocateKind { Found, Missing, Ambiguous, NotLiteral }

/// Where a name is declared. Start/Length span the value expression (the
/// literal and its leading minus), valid when Kind is Found. Count is the
/// number of declarations that carry the name.
public sealed record Located(LocateKind Kind, int Count = 0, int Start = 0, int Length = 0)
{
    public override string ToString() => Kind switch
    {
        LocateKind.Found => "literal",
        LocateKind.Missing => "no declaration",
        LocateKind.Ambiguous => $"declared {Count} times",
        _ => "not a numeric literal",
    };
}

/// The outcome of a rewrite: the new text, or the problem (Text unchanged).
public sealed record EditResult(string Text, string? Problem = null)
{
    public bool Ok => Problem is null;
}

public static class Literals
{
    /// Locate `name` in part.cs text. An explicit `new Param(`/`new Fit(` or
    /// any target-typed `new(` counts, unless its second argument is a
    /// string (a Body, Measurement or Annotation sharing the name); names in
    /// comments or other strings never match. A body or check sharing a
    /// parameter's name would make it Ambiguous, which is why names carry a
    /// dot.
    public static Located Locate(string text, string name)
    {
        var hits = Declarations(CSharpSyntaxTree.ParseText(text).GetRoot(), name);
        if (hits.Count == 0)
            return new(LocateKind.Missing);
        if (hits.Count > 1)
            return new(LocateKind.Ambiguous, hits.Count);
        var args = hits[0].ArgumentList!.Arguments;
        var value = args.Count > 1 ? args[1].Expression : null;
        return value is not null && IsNumber(value)
            ? new(LocateKind.Found, 1, value.SpanStart, value.Span.Length)
            : new(LocateKind.NotLiteral, 1);
    }

    /// Replace the numeric literal of `name` with `value`; every other byte
    /// of the file stays as it was.
    public static EditResult SetValue(string text, string name, double value)
    {
        var at = Locate(text, name);
        if (at.Kind != LocateKind.Found)
            return new(text, $"{name}: {at}");
        if (!double.IsFinite(value))
            return new(text, $"{name}: {value} is not a finite number");
        return new(text[..at.Start] + Format(value) + text[(at.Start + at.Length)..]);
    }

    /// Set the named argument `arg` (e.g. `Source`, `Ref`) of `name`'s
    /// declaration to the C# expression `expr`: replaces it when present,
    /// else replaces the positional argument at `position` (Param: Source 3,
    /// Ref 6) when one is given, else appends `, arg: expr` after the last
    /// argument.
    public static EditResult SetArgument(string text, string name, string arg, string expr, int position = -1)
    {
        var at = Locate(text, name);
        if (at.Kind != LocateKind.Found)
            return new(text, $"{name}: {at}");
        var decl = Declarations(CSharpSyntaxTree.ParseText(text).GetRoot(), name)[0];
        var args = decl.ArgumentList!.Arguments;
        var existing = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == arg);
        if (existing is null && position >= 0 && position < args.Count && args[position].NameColon is null)
            existing = args[position];
        if (existing is not null)
            return new(text[..existing.Expression.SpanStart] + expr + text[existing.Expression.Span.End..]);
        var end = args[^1].Span.End;
        return new(text[..end] + $", {arg}: {expr}" + text[end..]);
    }

    /// A C# number literal for `value`: invariant culture, round-trip exact,
    /// no exponent, at least one decimal digit (41 -> 41.0), `-` for negatives.
    public static string Format(double value)
    {
        if (value == 0)
            return "0.0";
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        if (s.Contains('E'))
            s = Expand(s);
        return s.Contains('.') ? s : s + ".0";
    }

    /// "1.5E-25" -> "0.00000000000000000000000015": the digits of the "R"
    /// string with the decimal point moved, nothing rounded.
    static string Expand(string r)
    {
        var sign = r[0] == '-' ? "-" : "";
        r = r.TrimStart('-');
        var e = r.IndexOf('E');
        var mantissa = r[..e];
        var dot = mantissa.IndexOf('.');
        var digits = mantissa.Replace(".", "");
        var point = (dot < 0 ? mantissa.Length : dot) + int.Parse(r[(e + 1)..], CultureInfo.InvariantCulture);
        return sign + (point <= 0 ? "0." + new string('0', -point) + digits
            : point >= digits.Length ? digits + new string('0', point - digits.Length) + ".0"
            : digits[..point] + "." + digits[point..]);
    }

    /// A correctly escaped C# string literal.
    public static string Quote(string s) => SymbolDisplay.FormatLiteral(s, quote: true);

    static List<BaseObjectCreationExpressionSyntax> Declarations(SyntaxNode root, string name) =>
        root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()
            .Where(n => n is ImplicitObjectCreationExpressionSyntax
                || (n is ObjectCreationExpressionSyntax o && o.Type is IdentifierNameSyntax { Identifier.Text: "Param" or "Fit" }))
            .Where(n => n.ArgumentList is { Arguments.Count: > 0 } al
                && al.Arguments[0].Expression is LiteralExpressionSyntax lit
                && lit.IsKind(SyntaxKind.StringLiteralExpression)
                && (string?)lit.Token.Value == name
                && !(al.Arguments.Count > 1 && al.Arguments[1].Expression is LiteralExpressionSyntax second
                    && second.IsKind(SyntaxKind.StringLiteralExpression)))
            .ToList();

    static bool IsNumber(ExpressionSyntax e) =>
        e is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression)
        || e is PrefixUnaryExpressionSyntax u && u.IsKind(SyntaxKind.UnaryMinusExpression)
            && u.Operand is LiteralExpressionSyntax inner && inner.IsKind(SyntaxKind.NumericLiteralExpression);
}
