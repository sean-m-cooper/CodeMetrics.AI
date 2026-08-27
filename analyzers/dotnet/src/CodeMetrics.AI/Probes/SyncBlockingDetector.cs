using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal enum SyncBlockingKind
{
    Result,
    GetAwaiterGetResult,
    Wait
}

internal sealed record SyncBlockingAccess(SyntaxNode Node, SyncBlockingKind Kind);

internal static class SyncBlockingDetector
{
    public static IReadOnlyList<SyncBlockingAccess> Find(
        SyntaxNode root,
        SemanticModel semanticModel,
        string suppressionCategory)
    {
        var accesses = new List<SyncBlockingAccess>();
        AddMemberAccesses(accesses, root, semanticModel, suppressionCategory);
        AddWaitInvocations(accesses, root, semanticModel, suppressionCategory);
        return accesses;
    }

    private static void AddMemberAccesses(
        ICollection<SyncBlockingAccess> accesses,
        SyntaxNode root,
        SemanticModel semanticModel,
        string suppressionCategory)
    {
        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!TryClassifyMemberAccess(memberAccess, semanticModel, out var receiver, out var kind) ||
                CompletedTaskAccess.IsKnownCompleted(memberAccess, receiver, semanticModel) ||
                FindingSuppression.IsSuppressed(memberAccess, suppressionCategory))
            {
                continue;
            }

            accesses.Add(new SyncBlockingAccess(memberAccess, kind));
        }
    }

    private static void AddWaitInvocations(
        ICollection<SyncBlockingAccess> accesses,
        SyntaxNode root,
        SemanticModel semanticModel,
        string suppressionCategory)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetWaitReceiver(invocation, semanticModel, out var receiver) ||
                CompletedTaskAccess.IsKnownCompleted(invocation, receiver, semanticModel) ||
                FindingSuppression.IsSuppressed(invocation, suppressionCategory))
            {
                continue;
            }

            accesses.Add(new SyncBlockingAccess(invocation, SyncBlockingKind.Wait));
        }
    }

    private static bool TryClassifyMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        out ExpressionSyntax receiver,
        out SyncBlockingKind kind)
    {
        receiver = memberAccess.Expression;
        kind = SyncBlockingKind.Result;
        if (memberAccess.Name.Identifier.Text == "Result")
            return TaskTypes.IsTaskLike(semanticModel.GetTypeInfo(receiver).Type);

        if (memberAccess.Name.Identifier.Text != "GetResult" ||
            memberAccess.Expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "GetAwaiter"
            } getAwaiter)
        {
            return false;
        }

        receiver = getAwaiter.Expression;
        kind = SyncBlockingKind.GetAwaiterGetResult;
        return TaskTypes.IsTaskLike(semanticModel.GetTypeInfo(receiver).Type);
    }

    private static bool TryGetWaitReceiver(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ExpressionSyntax receiver)
    {
        receiver = invocation.Expression;
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Wait"
            } waitAccess)
        {
            return false;
        }

        receiver = waitAccess.Expression;
        return TaskTypes.IsTaskLike(semanticModel.GetTypeInfo(receiver).Type);
    }
}
