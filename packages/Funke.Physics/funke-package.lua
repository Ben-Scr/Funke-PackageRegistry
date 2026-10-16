-- Funke.Physics: the Funke-Physics library (lightweight 2D bodies with box and
-- circle colliders, no rotation) as Fast Body 2D / Fast Box Collider 2D / Fast
-- Circle Collider 2D components plus the FastPhysics2D query API.
--
--   native - Pkg.Funke.Physics.Native: the library (Library/), the components,
--            scene hooks, the physics backend that runs next to Box2D, and the
--            extern "C" surface for the csharp layer (Native/).
--   csharp - Pkg.Funke.Physics: FastBody2D, FastBoxCollider2D,
--            FastCircleCollider2D and FastPhysics2D (Source/).
return {
    name = "Funke.Physics",
    version = "1.0.0",
    description = "Lightweight 2D physics for many simple bodies: Fast Body 2D, Fast Box/Circle Collider 2D, raycasts and overlap queries.",
    engine = ">=2026.1.0",
    modules = { "Funke.2D" },

    layers = {
        native = {
            sources = {
                "Library/src/**.cpp",
                "Library/src/**.hpp",
                "Library/include/**.hpp",
                "Native/**.cpp",
                "Native/**.hpp",
            },
            includes = {
                "Library/include",
                "Native",
            },
        },

        csharp = {
            sources = {
                "Source/**.cs",
            },
            allow_unsafe = true,
            pinvoke_dll = "Pkg.Funke.Physics.Native",
        },
    },

    dependencies = {},
}
