using System.Reflection;

namespace Snapdate.Architecture.Tests;

/// <summary>Guards the dependency rules from AGENTS.md.</summary>
public sealed class LayeringTests
{
    private static string[] ReferencesOf(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name is "netstandard" or "mscorlib";

    [Fact]
    public void Domain_references_only_the_framework()
    {
        Assert.All(ReferencesOf("Snapdate.Domain"), name => Assert.True(IsFrameworkAssembly(name), $"Domain references '{name}'."));
    }

    [Fact]
    public void Application_references_only_Domain_and_the_framework()
    {
        Assert.All(ReferencesOf("Snapdate.Application"), name =>
            Assert.True(IsFrameworkAssembly(name) || name == "Snapdate.Domain", $"Application references '{name}'."));
    }

    [Theory]
    [InlineData("Snapdate.Domain")]
    [InlineData("Snapdate.Application")]
    [InlineData("Snapdate.Infrastructure")]
    public void Core_layers_never_reference_the_UI(string assembly)
    {
        var references = ReferencesOf(assembly);
        Assert.DoesNotContain(references, r => r.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain("Snapdate", references);
    }
}
