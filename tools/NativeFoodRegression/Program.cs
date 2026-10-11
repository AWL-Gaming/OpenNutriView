using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Eco.Gameplay.Players;
using Eco.Shared.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NextFood = OpenNutriView.OpenNutriView.NextFood;

var root = Path.GetFullPath(args.Length == 1 ? args[0] : Directory.GetCurrentDirectory());
var nativeDir = Environment.GetEnvironmentVariable("ECO_NATIVE_DIR") ?? throw new Exception("ECO_NATIVE_DIR is required");
var dependencyDir = Environment.GetEnvironmentVariable("ECO_NATIVE_DEPENDENCY_DIR") ?? nativeDir;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { nativeDir, dependencyDir })
    {
        var path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
var product = FileVersionInfo.GetVersionInfo(Path.Combine(nativeDir, "Eco.Gameplay.dll")).ProductVersion;
if (product != "1.0.0+afb6b026c14454495459d823fb4bf26e21563370") throw new Exception("The native runtime must match Eco 1099 afb6b026");
Regression.Run(root);

static class Regression
{
[MethodImpl(MethodImplOptions.NoInlining)]
public static void Run(string root)
{
var checks = new List<string>();
void Check(string name, bool condition)
{
    if (!condition) throw new Exception(name);
    checks.Add(name);
    Console.WriteLine("PASS: " + name);
}
T Owned<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
Check("null stomach returns an empty native tooltip", NextFood.Stomach(null!) == LocString.Empty);
Check("unowned native stomach returns an empty tooltip", NextFood.Stomach(Owned<Stomach>()) == LocString.Empty);
Check("null player food tooltip is empty", NextFood.FoodItem(null!, null!) == LocString.Empty);

var cacheField = typeof(NextFood).GetField("StomachTooltipCache", BindingFlags.Static | BindingFlags.NonPublic)!;
var cache = cacheField.GetValue(null)!;
var entryType = typeof(NextFood).GetNestedType("StomachTooltipCacheEntry", BindingFlags.NonPublic)!;
var add = cache.GetType().GetMethod("Add")!;
User CacheUser(string message)
{
    var user = Owned<User>();
    var entry = Activator.CreateInstance(entryType, nonPublic: true)!;
    entryType.GetProperty("CreatedUtc")!.SetValue(entry, DateTime.UtcNow);
    entryType.GetProperty("Value")!.SetValue(entry, new LocString(message));
    add.Invoke(cache, new[] { user, entry });
    return user;
}
var firstUser = CacheUser("first cached tooltip");
var secondUser = CacheUser("second cached tooltip");
var first = Owned<Stomach>(); first.Owner = firstUser;
var second = Owned<Stomach>(); second.Owner = secondUser;
Check("actual native cache returns the owner's saved tooltip", NextFood.Stomach(first).ToString() == "first cached tooltip");
Check("users with identical uninitialized IDs keep separate cached tooltips", NextFood.Stomach(second).ToString() == "second cached tooltip");
Check("another user's cache cannot replace the first tooltip", NextFood.Stomach(first).ToString() == "first cached tooltip");
var failures = 0;
Parallel.For(0, 64, _ => { if (NextFood.Stomach(first).ToString() != "first cached tooltip") Interlocked.Increment(ref failures); });
Check("64 concurrent native cache reads preserve the tooltip", failures == 0);
Check("NA three-second cache lifetime is retained", (TimeSpan)typeof(NextFood).GetField("StomachTooltipCacheLifetime", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! == TimeSpan.FromSeconds(3));
var registration = OpenNutriView.OpenNutriViewPlugin.Register();
Check("native registration retains the mod name", registration.ModName == "OpenNutriView" && registration.ModDisplayName == "OpenNutriView");

CompilationUnitSyntax Parse(string path) => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.CSharp9)).GetRoot();
var source = Parse(Path.Combine(root, "OpenNutriView.cs"));
var accessible = source.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "GetAccessableFood");
var plugin = Parse(Path.Combine(root, "OpenNutriViewPlugin.cs"));
var constructor = plugin.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single(c => c.Identifier.ValueText == "OpenNutriViewPlugin");
var fakeSource = "using System; using System.Linq; using System.Collections.Generic; using System.Numerics;\n" +
    File.ReadAllText(Path.Combine(root, "tools", "NativeFoodRegression", "FoodSourceObjects.cs.txt")) +
    "\npublic class ConfigLoaderUnderTest { private readonly OpenNutriViewData data; public ConfigLoaderUnderTest() " + constructor.Body!.ToFullString() + " }\n" +
    "public static class FoodSourceUnderTest { " + accessible.ToFullString() +
    " public static Dictionary<Type,HashSet<WorldObject>> Query(User user,HashSet<int> currencies=null,float cost=float.PositiveInfinity,float distance=float.PositiveInfinity)=>GetAccessableFood(user,currencies,cost,distance); }\n" +
    File.ReadAllText(Path.Combine(root, "tools", "NativeFoodRegression", "FoodSourceCases.cs.txt"));
var systemNames = new[] { "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq", "System.Numerics.Vectors" };
var references = systemNames.Select(n => Assembly.Load(new AssemblyName(n)).Location).Distinct().Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("OpenNutriActualSourceFixtures", new[] { CSharpSyntaxTree.ParseText(fakeSource, new CSharpParseOptions(LanguageVersion.CSharp9)) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var emitted = compilation.Emit(output);
if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
var fixtureAssembly = Assembly.Load(output.ToArray());
foreach (var name in (string[])fixtureAssembly.GetType("FoodSourceCases")!.GetMethod("Run")!.Invoke(null, null)!) Check(name, true);
Console.WriteLine($"PASS: {checks.Count} OpenNutriView cases; native cache/registration and actual source census/storage fixtures. No server, save or live store accessed.");
}
}
