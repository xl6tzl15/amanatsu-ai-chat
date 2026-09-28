// IL2CPP's compatibility mscorlib lacks the nullable metadata attributes that
// Roslyn may emit for generated async state machines even with nullable disabled.
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field |
                AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue |
                AttributeTargets.GenericParameter)]
internal sealed class NullableAttribute : Attribute
{
    public NullableAttribute(byte value) { }
    public NullableAttribute(byte[] value) { }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
                AttributeTargets.Interface | AttributeTargets.Delegate)]
internal sealed class NullableContextAttribute : Attribute
{
    public NullableContextAttribute(byte value) { }
}
