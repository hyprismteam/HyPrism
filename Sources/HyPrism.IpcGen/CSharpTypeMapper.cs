using Microsoft.CodeAnalysis;

namespace HyPrism.IpcGen;

/// <summary>
/// Recursively maps Roslyn <see cref="ITypeSymbol"/> instances to TypeScript type strings
/// and accumulates <see cref="TsInterface"/> definitions for every named C# type it visits.
/// </summary>
/// <remarks>
/// Supported mappings:
/// <list type="bullet">
///   <item>Primitives: <c>bool → boolean</c>, <c>string/char → string</c>, numeric types → <c>number</c></item>
///   <item>Nullable reference types: <c>T? → T | null</c></item>
///   <item>Nullable value types (<c>Nullable&lt;T&gt;</c>): <c>T | null</c></item>
///   <item>Arrays and common collections (<c>List&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, …): <c>T[]</c></item>
///   <item>Dictionaries (<c>Dictionary&lt;K,V&gt;</c>, <c>IReadOnlyDictionary&lt;K,V&gt;</c>): <c>Record&lt;K,V&gt;</c></item>
///   <item><c>Task</c> / <c>Task&lt;T&gt;</c>: unwrapped to inner type or <c>void</c></item>
///   <item>Enums: string literal union, e.g. <c>'Idle' | 'Running' | 'Stopped'</c></item>
///   <item>Named classes and records: queued and emitted as <c>export interface</c> by <see cref="FlushQueue"/></item>
/// </list>
/// </remarks>
internal sealed class CSharpTypeMapper
{
    // Track interfaces already enqueued so we don't emit them twice
    private readonly HashSet<string> _seen = [];
    // Queue of named types to convert to TS interfaces
    private readonly Queue<INamedTypeSymbol> _queue = new();

    public List<TsInterface> Interfaces { get; } = [];

    #region Public API

    /// <summary>
    /// Converts a type symbol to its TypeScript representation.
    /// Named complex types are queued for interface generation.
    /// </summary>
    public string Map(ITypeSymbol type, bool forceOptional = false)
    {
        var ts = MapCore(type);
        return forceOptional ? ts : ts;
    }

    /// <summary>
    /// Processes all queued named types, generating TsInterface objects.
    /// Call after all methods have been mapped.
    /// </summary>
    public void FlushQueue()
    {
        while (_queue.Count > 0)
        {
            var sym = _queue.Dequeue();
            EmitInterface(sym);
        }
    }

    #endregion

    #region Core mapping

    private string MapCore(ITypeSymbol type)
    {
        // Nullable reference type (e.g. string?)
        if (type.NullableAnnotation == NullableAnnotation.Annotated
            && type.IsReferenceType)
        {
            var inner = MapCore(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            return $"{inner} | null";
        }

        // Nullable value type  T?  →  Nullable<T>
        if (type is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            return MapCore(named.TypeArguments[0]) + " | null";
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:   return "boolean";
            case SpecialType.System_String:    return "string";
            case SpecialType.System_Char:      return "string";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:   return "number";
            case SpecialType.System_Object:    return "unknown";
            case SpecialType.System_Void:      return "void";
        }

        if (type is IArrayTypeSymbol arr)
            return MapCore(arr.ElementType) + "[]";

        if (type is INamedTypeSymbol namedSym)
            return MapNamedType(namedSym);

        return "unknown";
    }

    private string MapNamedType(INamedTypeSymbol sym)
    {
        var fullName = sym.ToDisplayString();

        // Well-known generic collections
        if (sym.IsGenericType)
        {
            var def = sym.ConstructedFrom.ToDisplayString();

            // Dictionary<K,V> / IReadOnlyDictionary<K,V> → Record<K,V>
            if (def is "System.Collections.Generic.Dictionary<TKey, TValue>"
                    or "System.Collections.Generic.IDictionary<TKey, TValue>"
                    or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                return $"Record<{MapCore(sym.TypeArguments[0])}, {MapCore(sym.TypeArguments[1])}>";
            }

            // IEnumerable<T> / List<T> / IList<T> / IReadOnlyList<T> / ICollection<T> → T[]
            if (def is "System.Collections.Generic.IEnumerable<T>"
                    or "System.Collections.Generic.List<T>"
                    or "System.Collections.Generic.IList<T>"
                    or "System.Collections.Generic.IReadOnlyList<T>"
                    or "System.Collections.Generic.ICollection<T>"
                    or "System.Collections.Generic.IReadOnlyCollection<T>")
            {
                return MapCore(sym.TypeArguments[0]) + "[]";
            }

            // Task<T> → unwrap
            if (def is "System.Threading.Tasks.Task<TResult>")
                return MapCore(sym.TypeArguments[0]);

            // Action<T> → for IpcEvent emit delegates — caller handles this separately
            return "unknown";
        }

        // Task (non-generic) → void
        if (fullName == "System.Threading.Tasks.Task")
            return "void";

        // DateTime / DateTimeOffset → string (ISO serialized)
        if (fullName is "System.DateTime" or "System.DateTimeOffset")
            return "string";

        // TimeSpan → string
        if (fullName == "System.TimeSpan")
            return "string";

        // Enum: emit as string union
        if (sym.TypeKind == TypeKind.Enum)
        {
            var members = sym.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.IsConst)
                .Select(f => $"'{f.Name}'");
            return string.Join(" | ", members);
        }

        // Named class/record/struct — queue for interface emission
        if (sym.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            var shortName = sym.Name;
            if (_seen.Add(sym.ToDisplayString()))
                _queue.Enqueue(sym);
            return shortName;
        }

        return "unknown";
    }

    #endregion

    #region Interface emission

    private void EmitInterface(INamedTypeSymbol sym)
    {
        var fields = new List<TsField>();

        // Include all public properties (and positional record parameters, which are props too)
        foreach (var member in sym.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (member.IsStatic) continue;
            // Skip indexers
            if (member.IsIndexer) continue;

            var propType = member.Type;
            bool optional = propType.NullableAnnotation == NullableAnnotation.Annotated;
            var tsType = MapCore(propType);

            // camelCase the name
            var tsName = char.ToLowerInvariant(member.Name[0]) + member.Name[1..];
            fields.Add(new TsField(tsName, tsType, optional));
        }

        Interfaces.Add(new TsInterface(sym.Name, fields));

        // Flush any newly queued types discovered during property mapping
        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            EmitInterface(next);
        }
    }
    #endregion
}