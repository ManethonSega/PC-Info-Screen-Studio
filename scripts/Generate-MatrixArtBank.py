"""Adapt the supplied EPS sheets to exact LED grids. Requires Ghostscript and Pillow."""
import argparse
import json
import subprocess
import tempfile
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument("source_dir", type=Path)
parser.add_argument("--preview", type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
sheets = ["419266235_ebd3d78e-94a7-470c-9095-84b8f1e5145c.eps",
          "426110610_99db5b27-9cca-4bf4-a1e5-33b848eb7ead.eps",
          "426110618_f9b6e76a-7c58-4bf5-b78c-89e8314e945b.eps"]
choices = [
 ("heart", "Red heart", 0, (416,385,535,494)), ("pink-heart", "Pink heart", 1, (1034,90,1202,220)),
 ("star", "Star", 0, (580,372,700,491)), ("fire", "Fire", 0, (739,374,832,494)),
 ("mushroom", "Mushroom", 0, (370,205,471,329)), ("ghost", "Ghost", 0, (700,54,850,180)),
 ("cat", "Cat", 0, (242,365,364,495)), ("frog", "Frog", 0, (879,367,1020,495)),
 ("skull", "Skull", 0, (894,52,1018,181)), ("lightning", "Lightning", 0, (933,224,1018,330)),
 ("potion", "Green potion", 0, (550,52,659,179)), ("blue-potion", "Blue potion", 1, (719,44,852,246)),
 ("pink-potion", "Pink potion", 1, (857,43,996,249)), ("bomb", "Bomb", 1, (528,62,671,243)),
 ("coin", "Coin", 1, (1517,479,1653,628)), ("diamond", "Diamond", 2, (801,504,935,620)),
 ("gem", "Green gem", 2, (643,504,739,620)), ("ruby", "Ruby", 2, (469,504,579,625)),
 ("arrow-right", "Right arrow", 2, (505,378,680,505)), ("arrow-left", "Left arrow", 2, (700,378,868,504)),
 ("rocket", "Rocket", 1, (324,290,502,606)), ("cloud", "Cloud", 0, (199,250,335,334))]
choices = [c for c in choices if c[0] not in ("cat", "frog")]
choices.append(("hourglass", "Hourglass", 2, (70,227,213,379)))
bank = []
with tempfile.TemporaryDirectory() as tmp:
 images = []
 for i, name in enumerate(sheets):
  target = Path(tmp)/f"{i}.png"
  subprocess.run(["gs", "-q", "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dEPSCrop", "-sDEVICE=pngalpha", "-r96", f"-sOutputFile={target}", str(args.source_dir/name)], check=True)
  images.append(Image.open(target).convert("RGBA"))
 for key, label, sheet, box in choices:
  image = images[sheet].crop(box)
  bg = images[sheet].getpixel((0,0))[:3]
  data = image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata()
  image.putdata([(r,g,b,0 if sum((a-c)**2 for a,c in zip((r,g,b),bg)) < 2300 else 255) for r,g,b,_ in data])
  image = image.crop(image.getbbox())
  if key == "rocket": image = image.transpose(Image.Transpose.ROTATE_270)
  variants = []
  for height in (4,5,10):
   width = max(1, round(image.width*height/image.height))
   small = image.resize((width,height), Image.Resampling.BOX)
   # Quantize each exact grid rather than retaining blurry desktop antialiasing.
   quant = small.convert("RGB").quantize(colors=8).convert("RGB")
   palette, rows = [], []
   for y in range(height):
    row = ""
    for x in range(width):
     if small.getpixel((x,y))[3] < 80: row += "."; continue
     rgb = quant.getpixel((x,y))
     if max(rgb) < 45: rgb = (28,38,54)
     colour = "%02X%02X%02X" % rgb
     if colour not in palette: palette.append(colour)
     row += "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[palette.index(colour)]
    rows.append(row)
   variants.append(dict(Height=height, Width=width, Palette=palette, Rows=rows))
  # Tiny silhouettes are intentionally drawn on the native grid, preserving
  # the source's pink fins, grey hull, blue window and orange/yellow exhaust.
  if key == "rocket":
   palette = ["F030C8", "B8C7E5", "ECF6FF", "315BF4", "FFD828", "FD8F00"]
   shapes = {
    4: ["...P......", ".YO.SSSSP.", "YYOSBSSSPP", "...P......"],
    5: ["....P........", ".YO.PSSSSSP..", "YYOSSSBSSSSPP", ".YO.PSSSSSP..", "....P........"],
    10: ["......P.............", "......PP............", "......PSSSSSSS......", "...OO.PSSWSSSSSP....", ".OYYOOSSSBBSSSSSPP..", ".OYYYOSSSBBSSSSSPPP.", "...OO.PSSWSSSSSP....", "......PSSSSSSS......", "......PP............", "......P............."]}
   variants = [dict(Height=h, Width=max(map(len,rows)), Palette=palette, Rows=["".join("." if c=="." else str("PSWBYO".index(c)) for c in row.ljust(max(map(len,rows)),".")) for row in rows]) for h,rows in shapes.items()]
  if key in ("heart", "pink-heart", "star"):
   for v in variants:
    if v["Height"] == 10: continue
    h=v["Height"]
    rows = ([".0.0.","00000",".000.","..0.."] if h==4 else [".0.0.","00000","00000",".000.","..0.."]) if key!="star" else (["..0..","00000",".000.",".0.0."] if h==4 else ["..0..",".000.","00000",".000.",".0.0."])
    v.update(Width=5, Rows=rows, Palette=["FF334C" if key=="heart" else "F037CB" if key=="pink-heart" else "FFD132"])
  if key == "skull":
   shapes = {4:["00000","0.0.0",".000.",".0.0."],5:[".000.","00000","0.0.0",".000.",".0.0."],10:[".0000000.","000000000","000...000","00..0..00","00..0..00","000000000",".000.000.","..00000..","..0.0.0..","..0.0.0.."]}
   variants = [dict(Height=h, Width=len(rows[0]), Palette=["EDD9B6"], Rows=rows) for h,rows in shapes.items()]
  bank.append(dict(Key=key, Label=label, Variants=variants))
(root/"src/PCInfoScreenStudio.App/Assets/MatrixArtBank.json").write_text(json.dumps(bank, indent=2)+"\n")
if args.preview:
 canvas = Image.new("RGB", (720, len(bank)*85), "#091425"); draw = ImageDraw.Draw(canvas)
 for i, art in enumerate(bank):
  draw.text((8,i*85+6),art["Label"],fill="white")
  for j,v in enumerate(art["Variants"]):
   for y,row in enumerate(v["Rows"]):
    for x,ch in enumerate(row):
     if ch != ".": draw.rectangle((170+j*175+x*6,i*85+12+y*6,174+j*175+x*6,i*85+16+y*6),fill="#"+v["Palette"][int(ch,36)])
 canvas.save(args.preview)
print(f"Generated {len(bank)} assets, each at 4, 5 and 10 dots.")
