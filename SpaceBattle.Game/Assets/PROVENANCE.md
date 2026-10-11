English | [Русский](PROVENANCE.ru.md)

# Generated Ship Atlas

[Frontend](../README.md) |
[Benchmark report](../../docs/space-battle/REPORT.md) |
[Repository overview](../../README.md)

`ships-atlas.png` was generated with the built-in `image_gen.imagegen` tool,
with `transparent_background=true`, for this project. It is not a downloaded
third-party ship pack. The original image remains unchanged. The delivered
PNG is 1448 x 1086; the generator's spacing was nonuniform, so the renderer uses
the measured pixel rectangles in `ships-atlas.json`, preserving each aspect ratio.
Very faint alpha residue is discarded by the shader, without editing the PNG.

Classes 0-7 are escorts, 8-10 are carriers, and 11 is an unused shuttle sprite.
Team tinting, shields, engine trails, heat outlines and effects are rendered at
runtime. The atlas contains no HUD or baked battle scene.

## Generation Prompt

Use case: stylized-concept. Asset type: one production game sprite atlas for
top-down 2D space battle. Generate ONE transparent PNG sheet, landscape 4:3,
with EXACTLY 4 columns and 3 rows of equal square cells, each cell containing
ONE clearly separated spaceship viewed perfectly orthographic from above, all
bows pointing straight UP. No background, no ground shadow, no text, no labels,
no UI, no perspective, no glowing exhaust outside ships. Each silhouette
centered in its cell with generous transparent padding, max70% cell extent.
Detailed hand-painted hard-surface sci-fi sprites with sharp readable
silhouettes, dark charcoal metal, bright ivory armor panels, tiny teal cockpit
and engine accents, restrained red markings. Consistent lighting from upper
left. Row1 lefttoright: tiny narrow interceptor with swept wings; compact
twin-engine fighter; broad heavy bomber with missile pods; gunship with side
gun nacelles. Row2 lefttoright: corvette elongated four fins; frigate armored
diamond outline; heavy destroyer six gun platforms; support ship with curved
medical cargo wings. Row3 lefttoright: light carrier two landing bays; large
fleet carrier four hangars and wide triangular hull; foundry mothership
massive industrial spine and six branching modules; small angular shuttle.
Ships in bottom row appear larger within samecell and substantially more
elaborate, many visible hull sections. Transparent background trulyalpha.
Avoid cropping and avoid ships touching, one asset per cell, no grid lines.
This is a sprite atlas consumed directly by a game.
