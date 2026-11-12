using System.Reflection;

namespace Scheduling.Application;

/// <summary>Marker used to locate this assembly (architecture tests, validator scanning).</summary>
public static class ApplicationAssembly
{
    public static readonly Assembly Instance = typeof(ApplicationAssembly).Assembly;
}
