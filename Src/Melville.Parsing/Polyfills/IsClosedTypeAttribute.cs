#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Compiler-required attribute for the C# 15 <c>closed</c> modifier, which ships in the .NET 11 libraries.
/// The modifier is enforced at compile time only, so an internal copy lets the net10.0 build use it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class IsClosedTypeAttribute : Attribute
{
}
#endif
