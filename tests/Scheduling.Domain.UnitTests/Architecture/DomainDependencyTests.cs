namespace Scheduling.Domain.UnitTests.Architecture;

public class DomainDependencyTests
{
    [Fact]
    public void Domain_ReferencesOnlyTheBaseClassLibrary()
    {
        var references = DomainAssembly.Instance
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        references.Should().NotContain(name =>
            name.StartsWith("Scheduling.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }
}
