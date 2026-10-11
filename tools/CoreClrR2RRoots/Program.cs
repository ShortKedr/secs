using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CoreClrR2RRoots <linked-directory> <output.mibc>");
    return 1;
}

try
{
    using var generator = new RootGenerator(Path.GetFullPath(args[0]));
    generator.Write(Path.GetFullPath(args[1]));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

sealed class RootGenerator : IDisposable
{
    const string StoreInterface = "SpaceBattle.IEcsStore";
    static readonly string[] InputNames = ["SpaceBattle.Core.dll", "SpaceBattle.SECS.dll", "SECS.dll"];
    readonly string directory;
    readonly DefaultAssemblyResolver resolver = new();
    readonly AssemblyDefinition core;
    readonly AssemblyDefinition adapter;
    readonly TypeDefinition store;
    readonly SortedDictionary<string, Root> roots = new(StringComparer.Ordinal);
    readonly HashSet<string> scannedSites = new(StringComparer.Ordinal);
    readonly HashSet<string> mappedSites = new(StringComparer.Ordinal);

    public RootGenerator(string directory)
    {
        this.directory = directory;
        foreach (var name in InputNames)
            if (!File.Exists(Path.Combine(directory, name)))
                throw new FileNotFoundException("Missing linked input: " + Path.Combine(directory, name));
        resolver.AddSearchDirectory(directory);
        var parameters = new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false };
        core = AssemblyDefinition.ReadAssembly(Path.Combine(directory, "SpaceBattle.Core.dll"), parameters);
        adapter = AssemblyDefinition.ReadAssembly(Path.Combine(directory, "SpaceBattle.SECS.dll"), parameters);
        store = adapter.MainModule.GetType("SpaceBattle.SecsStore")
            ?? throw new InvalidOperationException("Linked adapter does not contain SpaceBattle.SecsStore.");
    }

    public void Write(string output)
    {
        if (!output.EndsWith(".mibc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must have a .mibc extension.");

        var methods = AllTypes(core.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        foreach (var method in methods)
        foreach (var instruction in method.Body.Instructions)
            if (instruction.Operand is GenericInstanceMethod call && call.DeclaringType.FullName == StoreInterface)
                scannedSites.Add(SiteKey(method, instruction));

        var pending = new Queue<Context>();
        foreach (var method in methods.Where(m => !m.HasGenericParameters))
        {
            var typeArguments = method.DeclaringType.GenericParameters.Select(p => CloseStoreParameter(p)).ToArray();
            if (typeArguments.Any(t => t.ContainsGenericParameter))
                continue;
            pending.Enqueue(new Context(method, typeArguments, []));
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var context))
        {
            var contextKey = context.Method.MetadataToken.ToInt32() + ":" +
                string.Join(";", context.TypeArguments.Concat(context.MethodArguments).Select(TypeKey));
            if (!visited.Add(contextKey))
                continue;

            foreach (var instruction in context.Method.Body.Instructions)
            {
                if (instruction.Operand is not GenericInstanceMethod call)
                    continue;
                var arguments = call.GenericArguments.Select(t => Substitute(t, context)).ToArray();
                if (arguments.Any(t => t.ContainsGenericParameter))
                    throw new InvalidOperationException($"Unclosed call in {context.Method.FullName}: {call.FullName}");

                if (call.DeclaringType.FullName == StoreInterface)
                {
                    var candidates = store.Methods.Where(m => m.Name == call.Name &&
                        m.GenericParameters.Count == arguments.Length && SignatureKey(m) == SignatureKey(call)).ToArray();
                    if (candidates.Length != 1)
                        throw new InvalidOperationException($"Cannot uniquely map {call.FullName} to SecsStore.");
                    var rootMethod = Instantiate(candidates[0], store, arguments);
                    var site = new CallSite(context.Method.FullName, context.Method.MetadataToken.ToInt32(),
                        instruction.Offset, call.FullName, context.MethodArguments.Select(TypeKey).ToArray());
                    AddRoot(rootMethod, "store-call", site);
                    mappedSites.Add(SiteKey(context.Method, instruction));
                    if (call.Name == "ForEach")
                        AddQueryRoots(candidates[0], arguments, site);
                }
                else if (call.Resolve() is { HasBody: true } definition && definition.Module == core.MainModule)
                {
                    var owner = Substitute(call.DeclaringType, context);
                    var ownerArguments = owner is GenericInstanceType instance
                        ? instance.GenericArguments.ToArray() : Array.Empty<TypeReference>();
                    pending.Enqueue(new Context(definition, ownerArguments, arguments));
                }
            }
        }

        var missing = scannedSites.Except(mappedSites).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException("Generic IEcsStore sites without closed IL callers: " + string.Join(", ", missing));
        if (!roots.Values.Any(r => r.Method.Name == "ForEach"))
            throw new InvalidOperationException("No ForEach roots found in linked Core IL.");

        using var mibc = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("CoreClrR2RStaticRoots", new Version(1, 0)),
            "CoreClrR2RStaticRoots.dll", ModuleKind.Dll);
        var module = mibc.MainModule;
        var dictionary = GlobalMethod(module, "AssemblyDictionary");
        var dictionaryIl = dictionary.Body.GetILProcessor();
        var groups = roots.Values.GroupBy(r => GroupKey(r.Method)).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < groups.Length; index++)
        {
            var group = GlobalMethod(module, $"Roots_{index:D4}");
            var il = group.Body.GetILProcessor();
            foreach (var root in groups[index])
            {
                il.Append(il.Create(OpCodes.Ldtoken, module.ImportReference(root.Method)));
                il.Append(il.Create(OpCodes.Pop));
            }
            il.Append(il.Create(OpCodes.Ret));
            dictionaryIl.Append(dictionaryIl.Create(OpCodes.Ldstr, groups[index].Key));
            dictionaryIl.Append(dictionaryIl.Create(OpCodes.Ldtoken, group));
            dictionaryIl.Append(dictionaryIl.Create(OpCodes.Pop));
            dictionaryIl.Append(dictionaryIl.Create(OpCodes.Pop));
        }
        dictionaryIl.Append(dictionaryIl.Create(OpCodes.Ret));

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // RC1 accepts an uncompressed PE MIBC; there is no execution or sample-weight data.
        mibc.Write(output, new WriterParameters { DeterministicMvid = true, Timestamp = 0 });
        var auditPath = Path.ChangeExtension(output, ".roots.json");
        var audit = new
        {
            formatVersion = 1,
            provenance = "static-il-roots-not-measured-pgo",
            encoding = "uncompressed-pe-mibc",
            substitution = "SpaceBattle.Battle<TStore> and nested types: TStore = SpaceBattle.SecsStore",
            inputs = InputNames.Select(name => new
            {
                file = name,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name))))
            }).ToArray(),
            genericInterfaceCallSiteCount = scannedSites.Count,
            expandedInterfaceCallSiteCount = roots.Values.Where(r => r.Kinds.Contains("store-call")).Sum(r => r.Sites.Count),
            rootCount = roots.Count,
            groups = groups.Select(g => new { assemblies = g.Key, rootCount = g.Count() }).ToArray(),
            roots = roots.Select(entry => new
            {
                key = entry.Key,
                method = entry.Value.Method.FullName,
                assembly = AssemblyName(entry.Value.Method.DeclaringType),
                declaringType = TypeKey(entry.Value.Method.DeclaringType),
                genericArguments = entry.Value.Method is GenericInstanceMethod instance
                    ? instance.GenericArguments.Select(TypeKey).ToArray() : [],
                kinds = entry.Value.Kinds.ToArray(),
                callSites = entry.Value.Sites.Values.ToArray()
            }).ToArray()
        };
        File.WriteAllText(auditPath, JsonSerializer.Serialize(audit, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) + "\n");
        Console.WriteLine($"{scannedSites.Count} generic IEcsStore IL sites; {roots.Count} closed roots in {groups.Length} groups.");
        foreach (var group in roots.Values.GroupBy(r => AssemblyName(r.Method.DeclaringType) + ":" + r.Method.Name).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        Console.WriteLine(output);
        Console.WriteLine(auditPath);
    }

    void AddQueryRoots(MethodDefinition method, TypeReference[] arguments, CallSite site)
    {
        var context = new Context(method, [], arguments);
        var queryCalls = method.Body.Instructions.Select(i => i.Operand).OfType<GenericInstanceMethod>()
            .Where(m => m.Name == "ForEach" && m.DeclaringType.Namespace == "SECS" && m.DeclaringType.Name.StartsWith("Query`", StringComparison.Ordinal));
        var count = 0;
        foreach (var call in queryCalls)
        {
            var owner = Substitute(call.DeclaringType, context);
            var queryArguments = call.GenericArguments.Select(t => Substitute(t, context)).ToArray();
            AddRoot(Instantiate(call.Resolve(), owner, queryArguments), "query-loop", site);
            var bridge = queryArguments.Single();
            var execute = bridge.Resolve().Methods.Single(m => m.Name == "Execute");
            AddRoot(Instantiate(execute, bridge, []), "bridge-execute", site);
            count++;
        }
        if (count != 1)
            throw new InvalidOperationException($"Expected one Query.ForEach call in {method.FullName}, found {count}.");
    }

    void AddRoot(MethodReference method, string kind, CallSite site)
    {
        var key = TypeKey(method.DeclaringType) + "::" + method.Name + "<" +
            (method is GenericInstanceMethod instance ? string.Join(",", instance.GenericArguments.Select(TypeKey)) : "") + ">";
        if (!roots.TryGetValue(key, out var root))
            roots.Add(key, root = new Root(method));
        root.Kinds.Add(kind);
        var siteKey = site.CallerToken + ":" + site.IlOffset + ":" + string.Join(";", site.CallerMethodArguments);
        root.Sites.TryAdd(siteKey, site);
    }

    TypeReference CloseStoreParameter(GenericParameter parameter) =>
        parameter.Type == GenericParameterType.Type && parameter.Name == "TStore" &&
        parameter.Owner is TypeReference owner && IsBattleType(owner) ? store : parameter;

    TypeReference Substitute(TypeReference type, Context context)
    {
        if (type is GenericParameter parameter)
        {
            var closed = CloseStoreParameter(parameter);
            if (closed != parameter)
                return closed;
            var arguments = parameter.Type == GenericParameterType.Method ? context.MethodArguments : context.TypeArguments;
            return parameter.Position < arguments.Length ? arguments[parameter.Position] : parameter;
        }
        if (type is GenericInstanceType instance)
        {
            var result = new GenericInstanceType(instance.ElementType);
            foreach (var argument in instance.GenericArguments)
                result.GenericArguments.Add(Substitute(argument, context));
            return result;
        }
        return type switch
        {
            ByReferenceType reference => new ByReferenceType(Substitute(reference.ElementType, context)),
            PointerType pointer => new PointerType(Substitute(pointer.ElementType, context)),
            ArrayType array => new ArrayType(Substitute(array.ElementType, context), array.Rank),
            RequiredModifierType modifier => new RequiredModifierType(modifier.ModifierType, Substitute(modifier.ElementType, context)),
            OptionalModifierType modifier => new OptionalModifierType(modifier.ModifierType, Substitute(modifier.ElementType, context)),
            _ => type
        };
    }

    static MethodReference Instantiate(MethodDefinition definition, TypeReference owner, TypeReference[] arguments)
    {
        var reference = new MethodReference(definition.Name, definition.ReturnType, owner)
        {
            HasThis = definition.HasThis, ExplicitThis = definition.ExplicitThis, CallingConvention = definition.CallingConvention
        };
        foreach (var parameter in definition.GenericParameters)
            reference.GenericParameters.Add(new GenericParameter(parameter.Name, reference));
        foreach (var parameter in definition.Parameters)
            reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        if (arguments.Length == 0)
            return reference;
        if (arguments.Length != definition.GenericParameters.Count)
            throw new InvalidOperationException("Generic method arity mismatch: " + definition.FullName);
        var instance = new GenericInstanceMethod(reference);
        foreach (var argument in arguments)
            instance.GenericArguments.Add(argument);
        return instance;
    }

    static string SignatureKey(MethodReference method) =>
        SignatureTypeKey(method.ReturnType) + "(" + string.Join(",", method.Parameters.Select(p => SignatureTypeKey(p.ParameterType))) + ")";

    static string SignatureTypeKey(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType instance => instance.ElementType.FullName + "<" + string.Join(",", instance.GenericArguments.Select(SignatureTypeKey)) + ">",
        ByReferenceType reference => SignatureTypeKey(reference.ElementType) + "&",
        PointerType pointer => SignatureTypeKey(pointer.ElementType) + "*",
        ArrayType array => SignatureTypeKey(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]",
        RequiredModifierType modifier => SignatureTypeKey(modifier.ElementType) + " modreq(" + modifier.ModifierType.FullName + ")",
        OptionalModifierType modifier => SignatureTypeKey(modifier.ElementType) + " modopt(" + modifier.ModifierType.FullName + ")",
        _ => type.FullName
    };

    static MethodDefinition GlobalMethod(ModuleDefinition module, string name)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        module.Types[0].Methods.Add(method);
        return method;
    }

    static string GroupKey(MethodReference method)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        AddTypeAssemblies(method.DeclaringType, names);
        if (method is GenericInstanceMethod instance)
            foreach (var argument in instance.GenericArguments)
                AddTypeAssemblies(argument, names);
        var defining = AssemblyName(method.DeclaringType);
        names.Remove(defining);
        return defining + ";" + string.Concat(names.Select(n => n + ";"));
    }

    static void AddTypeAssemblies(TypeReference type, ISet<string> names)
    {
        if (type.ContainsGenericParameter)
            throw new InvalidOperationException("Open root type: " + type.FullName);
        if (type is GenericInstanceType instance)
        {
            names.Add(AssemblyName(instance.ElementType));
            foreach (var argument in instance.GenericArguments)
                AddTypeAssemblies(argument, names);
        }
        else if (type is TypeSpecification specification)
            AddTypeAssemblies(specification.ElementType, names);
        else if (!type.IsPrimitive)
            names.Add(AssemblyName(type));
    }

    static string AssemblyName(TypeReference type) => type.Resolve().Module.Assembly.Name.Name;
    static string TypeKey(TypeReference type) => type is GenericInstanceType instance
        ? $"[{AssemblyName(instance.ElementType)}]{instance.ElementType.FullName}<{string.Join(",", instance.GenericArguments.Select(TypeKey))}>"
        : $"[{AssemblyName(type)}]{type.FullName}";
    static string SiteKey(MethodDefinition method, Instruction instruction) => $"0x{method.MetadataToken.ToInt32():x8}:IL_{instruction.Offset:x4}";
    static bool IsBattleType(TypeReference type) => type.FullName == "SpaceBattle.Battle`1" || type.FullName.StartsWith("SpaceBattle.Battle`1/", StringComparison.Ordinal);
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    public void Dispose()
    {
        core.Dispose();
        adapter.Dispose();
        resolver.Dispose();
    }

    sealed record Context(MethodDefinition Method, TypeReference[] TypeArguments, TypeReference[] MethodArguments);
    sealed record CallSite(string Caller, int CallerToken, int IlOffset, string InterfaceCall, string[] CallerMethodArguments);
    sealed class Root(MethodReference method)
    {
        public MethodReference Method { get; } = method;
        public SortedSet<string> Kinds { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, CallSite> Sites { get; } = new(StringComparer.Ordinal);
    }
}
