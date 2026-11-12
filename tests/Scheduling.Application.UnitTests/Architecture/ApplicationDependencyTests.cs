namespace Scheduling.Application.UnitTests.Architecture;

public class ApplicationDependencyTests
{
    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrWebConcerns()
    {
        var references = ApplicationAssembly.Instance
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        references.Should().NotContain(name =>
            name == "Scheduling.Infrastructure" ||
            name == "Scheduling.Api" ||
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            name.StartsWith("Npgsql", StringComparison.Ordinal));
    }
}
