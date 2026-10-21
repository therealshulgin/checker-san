"""Regenerate the code-drawn Checker San Windows icon (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
size = 1024
image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((16, 16, 1008, 1008), 224, fill="#211B26", outline="#6D4C66", width=16)
draw.polygon([(210, 360), (210, 274), (420, 274), (476, 340), (814, 340), (814, 742), (210, 742)], fill="#E6B2CB")
draw.rounded_rectangle((250, 398, 774, 702), 36, fill="#211B26")
draw.line([(394, 548), (477, 626), (651, 464)], fill="#E6B2CB", width=47, joint="curve")
for point in [(394, 548), (477, 626), (651, 464)]:
    draw.ellipse((point[0]-23, point[1]-23, point[0]+23, point[1]+23), fill="#E6B2CB")
out = root / "FolderChecker.App" / "Assets"
out.mkdir(exist_ok=True)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(out / "CheckerSan.ico", sizes=[(16,16), (24,24), (32,32), (48,48), (64,64), (128,128), (256,256)])
