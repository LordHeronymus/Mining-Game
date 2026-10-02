from pathlib import Path
from PIL import Image, ImageChops

sources = sorted(Path('Temp/TiefenhallHomeFrames').glob('*.png'))
assert len(sources) == 72
assert ImageChops.difference(Image.open(sources[0]).convert('RGB'), Image.open(sources[48]).convert('RGB')).getbbox()
frames = [Image.open(path).convert('RGB').resize((854, 480), Image.Resampling.LANCZOS) for path in sources[::2]]
palette = frames[len(frames) // 2].quantize(colors=192)
frames = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
output = Path('Assets/Design/Tiefenhall/Homescreen-motion.gif')
frames[0].save(output, save_all=True, append_images=frames[1:], duration=83, loop=0, optimize=True)
print(f'Captured animation: {len(frames)} frames, {output.stat().st_size:,} bytes. Frames visibly differ.')
