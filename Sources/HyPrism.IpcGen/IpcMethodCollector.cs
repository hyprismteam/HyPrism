using Microsoft.CodeAnalysis;

namespace HyPrism.IpcGen;

/// <summary>
/// Walks a Roslyn <see cref="Compilation"/>, finds all classes that inherit from
/// <c>IpcServiceBase</c>, and extracts <see cref="IpcMethod"/> descriptors from methods
/// decorated with <c>[IpcInvoke]</c>, <c>[IpcSend]</c>, or <c>[IpcEvent]</c> attributes.
/// </summary>
/// <remarks>
/// Channel metadata is read from attribute constructor arguments.
/// All type resolution is delegated to <see cref="CSharpTypeMapper"/>.
/// <list type="bullet">
///   <item><c>[IpcInvoke]</c> — return type becomes the TypeScript response type;
///         the first non-<c>CancellationToken</c> parameter becomes the input type.</item>
///   <item><c>[IpcSend]</c> — fire-and-forget; optional first parameter becomes the input type.</item>
///   <item><c>[IpcEvent]</c> — the sole parameter must be <c>Action&lt;T&gt;</c>;
///         <c>T</c> becomes the event data type emitted to the renderer.</item>
/// </list>
/// </remarks>
internal sealed class IpcMethodCollector(Compilation compilation, CSharpTypeMapper mapper)
{
    private const string IpcInvokeAttr = "HyPrism.Services.Core.Ipc.Attributes.IpcInvokeAttribute";
    private const string IpcSendAttr   = "HyPrism.Services.Core.Ipc.Attributes.IpcSendAttribute";
    private const string IpcEventAttr  = "HyPrism.Services.Core.Ipc.Attributes.IpcEventAttribute";
    private const string BaseClass     = "HyPrism.Services.Core.Ipc.IpcServiceBase";

    public List<IpcMethod> Collect()
    {
        var results = new List<IpcMethod>();

        // Find all types in the compilation that inherit from IpcServiceBase
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root  = tree.GetRoot();

            // Walk every named type in this syntax tree
            foreach (var typeDecl in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSym) continue;
                if (!InheritsFrom(typeSym, BaseClass)) continue;

                CollectFromType(typeSym, results);
            }
        }

        return results;
    }


    private void CollectFromType(INamedTypeSymbol typeSym, List<IpcMethod> results)
    {
        foreach (var member in typeSym.GetMembers().OfType<IMethodSymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString() ?? "";

                if (attrName == IpcInvokeAttr)
                {
                    results.Add(BuildInvoke(member, attr));
                    break;
                }
                if (attrName == IpcSendAttr)
                {
                    results.Add(BuildSend(member, attr));
                    break;
                }
                if (attrName == IpcEventAttr)
                {
                    results.Add(BuildEvent(member, attr));
                    break;
                }
            }
        }
    }

    private IpcMethod BuildInvoke(IMethodSymbol method, AttributeData attr)
    {
        var channel   = (string)attr.ConstructorArguments[0].Value!;
        var timeout   = attr.ConstructorArguments.Length > 1
            ? (int)attr.ConstructorArguments[1].Value!
            : 10_000;

        var returnTs  = UnwrapTask(method.ReturnType);
        var inputTs   = GetInputTsType(method);

        return new IpcMethod(IpcKind.Invoke, channel, timeout, inputTs, returnTs);
    }

    private IpcMethod BuildSend(IMethodSymbol method, AttributeData attr)
    {
        var channel  = (string)attr.ConstructorArguments[0].Value!;
        var inputTs  = GetInputTsType(method);

        return new IpcMethod(IpcKind.Send, channel, 0, inputTs, "void");
    }

    private IpcMethod BuildEvent(IMethodSymbol method, AttributeData attr)
    {
        var channel = (string)attr.ConstructorArguments[0].Value!;

        // Parameter must be Action<T> — extract T
        var param = method.Parameters.FirstOrDefault();
        string dataTs = "unknown";
        if (param?.Type is INamedTypeSymbol { IsGenericType: true } actionSym
            && actionSym.ConstructedFrom.ToDisplayString()
                is "System.Action<T>")
        {
            dataTs = mapper.Map(actionSym.TypeArguments[0]);
        }

        return new IpcMethod(IpcKind.Event, channel, 0, null, dataTs);
    }

    #region Helpers

    private string UnwrapTask(ITypeSymbol ret)
    {
        if (ret is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.SpecialType == SpecialType.None
            && named.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
        {
            return mapper.Map(named.TypeArguments[0]);
        }

        if (ret.ToDisplayString() == "System.Threading.Tasks.Task")
            return "void";

        return mapper.Map(ret);
    }

    private string? GetInputTsType(IMethodSymbol method)
    {
        // Skip CancellationToken parameters; take the first remaining one
        var relevant = method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToArray();

        if (relevant.Length == 0) return null;

        var param = relevant[0];
        // string input: just "string"
        if (param.Type.SpecialType == SpecialType.System_String)
            return "string";

        return mapper.Map(param.Type);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string baseFullName)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == baseFullName) return true;
            current = current.BaseType;
        }
        return false;
    }
    #endregion
}