using System.Runtime.CompilerServices;

namespace Funke.Physics;

internal static class PackageRegistration
{
    // Names match the display names Native/PackageEntry.cpp registers, so GetComponent/AddComponent
    // resolve the wrappers like built-in components. ScriptCore runs this initializer before a lookup
    // could miss, which is the host-integration case CA2255 allows.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
    {
        Entity.RegisterNativeComponent<FastBody2D>("Fast Body 2D");
        Entity.RegisterNativeComponent<FastBoxCollider2D>("Fast Box Collider 2D");
        Entity.RegisterNativeComponent<FastCircleCollider2D>("Fast Circle Collider 2D");
    }
}
