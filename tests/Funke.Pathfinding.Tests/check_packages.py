import json
import re
import sys
from pathlib import Path

engine, fixture, output = map(lambda value: Path(value).resolve(), sys.argv[1:])
sys.path.insert(0, str(engine / "scripts/platform"))
from funke_platform_sdk.packages import project_packages
from funke_platform_sdk.native_packages import _native_package, _registry_source

packages = project_packages(engine, fixture)
assert [p.name for p in packages] == ["Funke.Pathfinding", "Funke.Pathfinding3D"]
assert set(packages[1].manifest['modules']) == {"Funke.3D", "Funke.Physics3D"}
native = [n for p in packages if (n := _native_package(p))]
bindings = fixture / "Packages/Funke.Pathfinding/Source/NativeBindings.cs"
expected = set(re.findall(r'EntryPoint = "([^"]+)"', bindings.read_text()))
assert len(native) == 1 and not native[0].links_engine
assert len(expected) == 12 and set(native[0].exports) == expected
output.mkdir(parents=True, exist_ok=True)
(output / "static-registry.cpp").write_text(_registry_source(native, True))
(output / "static-exports.json").write_text(json.dumps({
    "status": "passed", "packages": [p.name for p in packages], "exports": sorted(expected)
}, indent=2))
print("Static package scan: dependency order and all 12 managed exports passed.")
