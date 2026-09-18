using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JevGen.CodeFixes;

/// <summary>
/// Fixes the JevGen diagnostics whose correction is unambiguous.
/// </summary>
/// <remarks>
/// Only mechanical corrections are offered. Anything requiring a judgement the compiler cannot
/// make — which of several parameters is the state, what an enum option's criteria should say —
/// is left to the developer, because a plausible-looking wrong fix in an AI contract is worse
/// than no fix at all.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(JevCodeFixProvider)), Shared]
public sealed class JevCodeFixProvider : CodeFixProvider
{
    private const string AddClientTitle = "Add [JevClient]";
    private const string AddStateTitle = "Add [State]";
    private const string AddOptionTitle = "Add [JevOption]";
    private const string AddTokenTitle = "Add a CancellationToken parameter";
    private const string MoveTokenTitle = "Move the CancellationToken to the end";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("JEV004", "JEV007", "JEV011", "JEV018", "JEV019");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => Microsoft.CodeAnalysis.CodeFixes.WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            switch (diagnostic.Id)
            {
                case "JEV018":
                    RegisterAddClient(context, diagnostic, node);
                    break;

                case "JEV004":
                    RegisterAddState(context, diagnostic, node);
                    break;

                case "JEV007":
                    RegisterAddOption(context, diagnostic, node);
                    break;

                case "JEV019":
                    RegisterAddToken(context, diagnostic, node);
                    break;

                case "JEV011":
                    RegisterMoveToken(context, diagnostic, node);
                    break;
            }
        }
    }

    private static void RegisterAddClient(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<InterfaceDeclarationSyntax>() is not { } declaration)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                AddClientTitle,
                token => AddAttributeAsync(context.Document, declaration, "JevClient", token),
                equivalenceKey: AddClientTitle),
            diagnostic);
    }

    private static void RegisterAddState(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method)
        {
            return;
        }

        // Offer to mark each candidate, so the developer chooses which parameter is the state
        // rather than the fix guessing.
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (IsCancellationToken(parameter) || HasAttribute(parameter.AttributeLists, "State"))
            {
                continue;
            }

            var captured = parameter;
            var title = $"{AddStateTitle} to '{parameter.Identifier.ValueText}'";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    token => AddParameterAttributeAsync(context.Document, captured, "State", token),
                    equivalenceKey: title),
                diagnostic);
        }
    }

    private static void RegisterAddOption(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<EnumMemberDeclarationSyntax>() is not { } member)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                AddOptionTitle,
                token => AddOptionAsync(context.Document, member, token),
                equivalenceKey: AddOptionTitle),
            diagnostic);
    }

    private static void RegisterAddToken(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                AddTokenTitle,
                token => AddCancellationTokenAsync(context.Document, method, token),
                equivalenceKey: AddTokenTitle),
            diagnostic);
    }

    private static void RegisterMoveToken(CodeFixContext context, Diagnostic diagnostic, SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                MoveTokenTitle,
                token => MoveCancellationTokenAsync(context.Document, method, token),
                equivalenceKey: MoveTokenTitle),
            diagnostic);
    }

    private static async Task<Document> AddAttributeAsync(
        Document document,
        InterfaceDeclarationSyntax declaration,
        string attributeName,
        CancellationToken cancellationToken)
    {
        var attribute = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attributeName))));

        var updated = declaration
            .WithAttributeLists(declaration.AttributeLists.Insert(0, attribute))
            .WithTriviaFrom(declaration);

        return await ReplaceAsync(document, declaration, updated, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> AddParameterAttributeAsync(
        Document document,
        ParameterSyntax parameter,
        string attributeName,
        CancellationToken cancellationToken)
    {
        var attribute = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attributeName))));

        var updated = parameter
            .WithAttributeLists(parameter.AttributeLists.Insert(0, attribute))
            .WithTriviaFrom(parameter);

        return await ReplaceAsync(document, parameter, updated, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> AddOptionAsync(
        Document document,
        EnumMemberDeclarationSyntax member,
        CancellationToken cancellationToken)
    {
        // The identifier is derivable; the criteria are not, so a placeholder makes the gap
        // obvious rather than shipping an empty description that looks intentional.
        var identifier = ToOptionId(member.Identifier.ValueText);

        var attribute = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("JevOption"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SeparatedList(
                        [
                            Argument(identifier),
                            Argument($"TODO: describe when {member.Identifier.ValueText} applies"),
                        ])))));

        var updated = member
            .WithAttributeLists(member.AttributeLists.Insert(0, attribute))
            .WithTriviaFrom(member);

        return await ReplaceAsync(document, member, updated, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> AddCancellationTokenAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var parameter = SyntaxFactory
            .Parameter(SyntaxFactory.Identifier("cancellationToken"))
            .WithType(SyntaxFactory.ParseTypeName("global::System.Threading.CancellationToken"))
            .WithDefault(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)));

        var updated = method.WithParameterList(
            method.ParameterList.AddParameters(parameter));

        return await ReplaceAsync(document, method, updated, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> MoveCancellationTokenAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var parameters = method.ParameterList.Parameters.ToList();
        var token = parameters.FirstOrDefault(IsCancellationToken);

        if (token is null)
        {
            return document;
        }

        parameters.Remove(token);
        parameters.Add(token);

        var updated = method.WithParameterList(
            method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(parameters)));

        return await ReplaceAsync(document, method, updated, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        SyntaxNode original,
        SyntaxNode replacement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(original, replacement));
    }

    private static AttributeArgumentSyntax Argument(string value)
        => SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(value)));

    private static bool IsCancellationToken(ParameterSyntax parameter)
        => parameter.Type?.ToString().EndsWith("CancellationToken", System.StringComparison.Ordinal) == true;

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name)
        => lists.SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() is var text
                              && (text == name || text == name + "Attribute"));

    private static string ToOptionId(string memberName)
        => memberName.Length > 0
            ? char.ToLowerInvariant(memberName[0]) + memberName.Substring(1)
            : memberName;
}
