// Static check that a plugin DLL will bind against a given game build (flat-screen or VR).
//
// Verifies:
//   1. every type/member the plugin references in the game's assemblies resolves in that build;
//   2. every [HarmonyPatch(typeof(T), "Method", ...)] target exists;
//   3. extra by-name lookups ("Type::member", from a text file) exist — for AccessTools/Traverse string lookups.
//
// Usage: dotnet run --project scripts/RefCheck -- <plugin.dll> <game Managed dir> [names.txt]
using Mono.Cecil;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: RefCheck <plugin.dll> <ManagedDir> [names.txt]");
    return 2;
}

var pluginPath = args[0];
var managedDir = args[1];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managedDir);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(pluginPath))!);
var plugin = ModuleDefinition.ReadModule(pluginPath, new ReaderParameters { AssemblyResolver = resolver });
var gameAsm = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
var gameScopes = new HashSet<string> { "Assembly-CSharp", "bolt", "bolt.user", "UnityEngine" };
int failures = 0, checks = 0;

void Fail(string what)
{
    failures++;
    Console.WriteLine($"  MISSING  {what}");
}

static string ScopeName(TypeReference t) => t.Scope switch
{
    AssemblyNameReference a => a.Name,
    ModuleDefinition m => m.Assembly.Name.Name,
    _ => t.Scope?.Name ?? "",
};

// 1. Compiled references
foreach (var tr in plugin.GetTypeReferences().Where(t => gameScopes.Contains(ScopeName(t))))
{
    checks++;
    if (tr.Resolve() == null) Fail($"type {tr.FullName}");
}
foreach (var mr in plugin.GetMemberReferences())
{
    var declaring = mr.DeclaringType;
    while (declaring is TypeSpecification ts) declaring = ts.ElementType;
    if (!gameScopes.Contains(ScopeName(declaring))) continue;
    checks++;
    try
    {
        var resolved = mr switch
        {
            MethodReference m => (IMemberDefinition?)m.Resolve(),
            FieldReference f => f.Resolve(),
            _ => null,
        };
        if (resolved == null) Fail($"{mr.GetType().Name.Replace("Reference", "")} {mr.FullName}");
    }
    catch (Exception e)
    {
        Fail($"{mr.FullName} ({e.Message})");
    }
}

// 2. HarmonyPatch attribute targets
foreach (var type in plugin.Types)
{
    foreach (var attr in type.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch"))
    {
        var targetType = attr.ConstructorArguments.Select(a => a.Value).OfType<TypeReference>().FirstOrDefault()?.Resolve();
        var methodName = attr.ConstructorArguments.Select(a => a.Value).OfType<string>().FirstOrDefault();
        if (targetType == null || methodName == null) continue;
        checks++;
        var paramTypes = attr.ConstructorArguments.Skip(2).Select(a => a.Value).OfType<CustomAttributeArgument[]>()
            .SelectMany(x => x).Select(x => ((TypeReference)x.Value).FullName).ToList();
        if (paramTypes.Count == 0 && attr.ConstructorArguments.Count > 2 && attr.ConstructorArguments[2].Value is TypeReference single)
            paramTypes.Add(single.FullName);
        var matches = targetType.Methods.Where(m => m.Name == methodName &&
            (paramTypes.Count == 0 || m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(paramTypes))).ToList();
        if (matches.Count == 0) Fail($"patch target {targetType.FullName}::{methodName}({string.Join(",", paramTypes)}) for {type.Name}");
        else if (matches.Count > 1) Fail($"ambiguous patch target {targetType.FullName}::{methodName} for {type.Name}");
    }
}

// 3. Name-based lookups
if (args.Length > 2)
{
    foreach (var raw in File.ReadAllLines(args[2]))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var optional = line.StartsWith('?');
        if (optional) line = line[1..];
        checks++;
        var parts = line.Split("::");
        var t = gameAsm.MainModule.GetType(parts[0]);
        if (t == null)
        {
            if (!optional) Fail($"type {parts[0]}");
            continue;
        }
        if (parts.Length == 1) continue;
        var name = parts[1];
        bool found = false;
        for (var cur = t; cur != null && !found; cur = cur.BaseType?.Resolve())
        {
            found = cur.Methods.Any(m => m.Name == name) || cur.Fields.Any(f => f.Name == name)
                    || cur.Properties.Any(p => p.Name == name) || cur.NestedTypes.Any(n => n.Name == name);
        }
        if (!found && !optional) Fail(line);
    }
}

Console.WriteLine(failures == 0 ? $"OK: {checks} checks passed against {managedDir}" : $"FAILED: {failures}/{checks} checks against {managedDir}");
return failures == 0 ? 0 : 1;
