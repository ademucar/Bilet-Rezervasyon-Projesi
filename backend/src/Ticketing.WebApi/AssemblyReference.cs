using System.Reflection;

namespace Ticketing.WebApi;

/// <summary>
/// Bkz. diger katmanlardaki AssemblyReference açıklaması.
/// </summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
