import argparse
import json
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--engine", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
engine = args.engine.resolve()
project = args.output.resolve()
package = Path(__file__).resolve().parents[1]
if not (project / "project.json").exists():
    subprocess.run([str(engine / "bin/Release-windows-x86_64/Funke-CLI/funke.exe"), "create", project.name,
                    "--path", str(project.parent), "--no-git", "--no-compile", "--no-register"], check=True)
config = json.loads((project / "project.json").read_text(encoding="utf-8-sig"))
config.update(packages=["Funke.Lighting2D"], modules=["Funke.2D"], buildWidth=960, buildHeight=600,
              buildFullscreen=False, buildRunInBackground=True, uiReferenceWidth=960, uiReferenceHeight=600)
config["splashScreen"] = {"enabled": False}
(project / "project.json").write_text(json.dumps(config, indent=2), encoding="utf-8")
shutil.copytree(package, project / "Packages/Funke.Lighting2D", dirs_exist_ok=True,
                ignore=shutil.ignore_patterns("Examples", "Tests", "__pycache__", "Bin"))
shutil.copyfile(package / "Examples/LightingSmoke.cs", project / "Assets/Scripts/LightingSmoke.cs")

def entity(name, x=0, y=0, sx=1, sy=1, **components):
    return dict(name=name, Transform2D=dict(posX=x, posY=y, scaleX=sx, scaleY=sy), **components)

entities = [entity("Camera", Camera2D=dict(orthoSize=5, zoom=1, clearR=0.3, clearG=0.3, clearB=0.3))]
for y in range(-5, 6):
    for x in range(-9, 10):
        shade = 0.7 if (x+y) % 2 == 0 else 0.5
        entities.append(entity(f"Floor {x} {y}", x, y, 0.96, 0.96,
            SpriteRenderer=dict(r=shade, g=shade, b=shade, a=1, sortOrder=-10)))
entities.extend([
    entity("Warm Light", -3, 1.2, Light2D=dict(r=1, g=0.5, b=0.16, intensity=3, radius=7, falloff=1.4, castShadows=True)),
    entity("Cool Light", 3, -1.2, Light2D=dict(r=0.15, g=0.7, b=1, intensity=3, radius=7, falloff=1.4, castShadows=True)),
    entity("Ambient", AmbientLight2D=dict(r=0.7, g=0.75, b=1, intensity=0.12)),
    entity("Wall", 0, 0, ShadowOccluder2D=dict(sizeX=0.65, sizeY=3, opacity=1)),
    entity("Wall Visual", 0, 0, 0.65, 3, SpriteRenderer=dict(r=0.8, g=0.8, b=0.8, a=1, sortOrder=5)),
    entity("Tests", Scripts=[dict(className="LightingSmoke", type="Managed")]),
    dict(name="Unlit UI", RectTransform2D=dict(anchorMinX=0, anchorMinY=1, anchorMaxX=0, anchorMaxY=1,
         pivotX=0, pivotY=1, posX=20, posY=-20, sizeX=100, sizeY=20), Image=dict(r=1,g=1,b=1,a=1))
])
for index, item in enumerate(entities, start=1):
    item["uuid"] = str(800000 + index)
(project / "Assets/Scenes/SampleScene.scene").write_text(json.dumps(dict(version=1,name="SampleScene",systems=[],entities=entities),indent=2),encoding="utf-8")
subprocess.run([str(engine / "vendor/bin/premake5.exe"), "--file=" + str(engine / "premake/project-packages.lua"),
                "--funke-project=" + str(project), "vs2022"], cwd=engine, check=True)
print(project)
