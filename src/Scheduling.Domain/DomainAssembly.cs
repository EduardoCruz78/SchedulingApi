using System.Reflection;

namespace Scheduling.Domain;

/// <summary>Marker used to locate this assembly (architecture tests, DI scanning).</summary>
public static class DomainAssembly
{
    public static readonly Assembly Instance = typeof(DomainAssembly).Assembly;
}
