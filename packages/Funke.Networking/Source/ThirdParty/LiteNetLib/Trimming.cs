// Vendored from LiteNetLib 2.1.4 (MIT, see LICENSE.txt). Changes: types internal, nullable off.
#nullable disable
#pragma warning disable CS0649
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using static System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes;

namespace LiteNetLib
{
    internal static class Trimming
    {
        internal const DynamicallyAccessedMemberTypes SerializerMemberTypes = PublicProperties | NonPublicProperties;
    }
}
#endif
