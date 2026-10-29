import argparse
from pathlib import Path
from PIL import Image, ImageChops

parser = argparse.ArgumentParser()
parser.add_argument("screenshots", type=Path)
args = parser.parse_args()
images = [Image.open(args.screenshots / f"at-{time}.png").convert("RGB") for time in (1, 3, 5, 7)]
shadowed, unshadowed, disabled, restored = images
assert all(image.size == (960, 600) for image in images)
assert all(image.getpixel((30, 25)) == (255, 255, 255) for image in images), "UI changed with world lighting"
warm = shadowed.getpixel((300, 240))
cool = shadowed.getpixel((660, 360))
assert warm[0] > warm[2] * 1.8, "warm light is missing"
assert cool[2] > cool[0] * 1.8, "cool light is missing"
assert unshadowed.getpixel((550, 200))[0] > shadowed.getpixel((550, 200))[0] + 70, "occluder casts no shadow"
gray = disabled.getpixel((300, 240))
assert max(gray) - min(gray) <= 1, "disabling lights did not restore the scene"
assert ImageChops.difference(shadowed, restored).getbbox() is None, "reenabling lighting retained stale state"
print("PASS: warm/cool lights, occluder shadows, unchanged UI, disabled bypass, restored image")
