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

## Gun Atlas

`guns-atlas.png` is a separate, original generated asset, created for this project
with the built-in `image_gen.imagegen` tool and `transparent_background=true`.
The unedited RGBA PNG is 1024 x 1536. Its [exact prompt](guns-prompt.txt) is saved
alongside the asset. No downloaded weapon pack was used.

[guns-atlas.json](guns-atlas.json) contains measured, nonuniform crop rectangles
with four pixels of padding and crop-relative rotation pivots. Kind -1 is the
fixed mounting pedestal; kinds 0-4 match MachineGun, Cannon, Plasma, Missile and
HeavyCannon. Current mounted gameplay uses Cannon and Plasma; the other ordinary
heads are available artwork, not newly enabled weapons. HeavyCannon is used by
the hull gun. Every head points up in the source and rotates around its bearing,
with the existing recoil offset; the pedestal stays fixed.

Both clients consume the same PNG and manifest. Native packs ship and gun pixels
into one runtime GL texture, retaining a single ordered draw batch. Browser
alpha-discard preprocessing happens once at load, matching the native threshold
of 32; neither client modifies the source PNG. Tiny runtime heat/team bearing
indicators are separate from the neutral ivory/charcoal artwork. This visual
change does not change entities, weapon tuning, physics or frozen measurements.

## Effects Atlas

`effects-atlas.png` is an original transparent RGBA image generated for this
project with the built-in `image_gen.imagegen` tool and
`transparent_background=true`. The unedited output is 1254 x 1254, rather than
the requested 1024 x 1024. The [initial prompt](effects-prompt.txt) and the
[contrast edit prompt](effects-contrast-prompt.txt) are retained. The current
PNG is the unmodified output of that built-in imagegen edit: brighter colored
cores and clearer dark/light separation, without a broad white bloom.
No third-party projectile or particle pack was downloaded.

[effects-atlas.json](effects-atlas.json) maps five `projectile` kinds 0-4
(MachineGun, Cannon, Plasma, Missile, HeavyCannon) and four `particle` kinds 0-3
(spark, plasma splash, explosion puff, heavy impact). Projectile crops were
measured from alpha above 32 with four pixels of padding. Particle crops are
square, with centered pivots and at least four pixels of transparent padding;
square cells come from padding, not stretching the raster. Projectiles point right, with
the head anchored to the entity position and the tail behind it. Their source
aspect ratios are preserved, actual velocity steers them, and the existing
lifetime alpha fades square particle quads.

Native packs all three atlases into one 1448 x 3876 GL texture and one ordered
draw batch. Browser alpha cleanup happens once on load; the axis-aligned
particle path avoids per-particle transform saves. No pixel processing occurs
per frame. Native uses separate projectile/particle lookup arrays, like the
browser's separate maps: kind 0 in one family cannot select kind 0 in the other.
Both loaders reject non-square or off-center particle cells. Sprites replace
primitive projectile strokes/circles and texture the particle squares, not
neutral gray debris. Physics, damage, entities, simulation RNG,
gameplay revision and frozen measurements are unchanged.

Validation: [asset tests](../../tools/space-battle/test_effect_atlas.py),
[native framebuffer regression](../EffectRenderValidation.cs) and
[browser pixel/heading/fade tests](../../tools/web/check-effects.mjs).

### Contrast Checks

[Contrast regression](../../tools/web/check-effect-contrast.mjs) compares the
old/new AOT clients at identical seeds, profiles, ticks and camera zooms. Hidden
effect draws provide a reference without changing simulation state, separating
VFX brightness from ship artwork, stars and HUD. Nine isolated sprite probes
also report strong-pixel coverage (contrast ratio at least 3) and the 75th
percentile of effect-added linear-sRGB luminance across identical 128-pixel
probe rectangles, including transparent pixels. The group-median percentile
target is at least 10% improvement for both projectiles and particles; this is
a chosen readability target, not an accessibility certification.

Fixed artistic brightness budgets are chosen before inspecting the candidate:
mean effect-added scene luminance at most 0.03, effect-attributable hot pixels
at most 1% of the scene, and at most 25% of each non-overlapping 64 x 64 pixel
tile on a fixed grid (partial edge tiles use actual area). A hot pixel
has final linear Rec.709 luminance at least 0.8 and an effect-added increase at
least 0.05. The whole-scene budget limits washout; the tile limit catches a
localized bright blob that a scene average could hide. Normal blending, lifetime
alpha and simulation-defined particle sizes remain unchanged. These limits are
visual regression heuristics, not universal comfort or medical safety claims;
inspect actual desktop/mobile and dense battle screenshots as well.

The [hash-bound result snapshot](effects-contrast-checks.json) records 12 paired
candidate scenes: low/nominal/high, ticks 300/600, zooms 1/3, seed 7331. Maximum
added Y is 0.002015; hot scene coverage is 0.0486%; hottest tile is 1.5625%.
Group-median probe gains are 134.1% for projectiles and 77.1% for particles,
not uniform gains at every pixel or a gameplay-performance claim. Explosion
kind 2 has a 2.45% lower p75 Y with a slightly larger strong-pixel fraction;
heavy-impact kind 3 has a smaller strong-pixel fraction (21.77% to 19.81%) but
a 22.25% higher p75 Y. Darker edges and localized cores are intentional, and
these individual changes are retained rather than hidden by the family median.

## Warp Atlas

The [warp PNG](warp-atlas.png) was generated with the built-in imagegen tool
from the stored prompt, without external image references. The selected output
is preserved unchanged: 1254 x 1254 RGBA, with genuine transparent pixels.
The [manifest](warp-atlas.json) measures four 627-pixel square cells. The prompt's
1024-pixel size and 72% extent targets were not exact output constraints; loading
uses the actual dimensions and measured transparent borders instead.

Warp artwork has its own `warp-atlas.png` and `warp-atlas.json`, separate from
projectiles and explosions. Its [production prompt](warp-prompt.txt) specifies
four transparent square cells: cyan aperture, red aperture, cyan arrival and
red arrival, in row-major order. The crop-relative pivots are centered and the
shared alpha threshold is 32. Native packing places this sheet to the right
of the existing atlas stack, preserving the 4096-pixel dimension budget.

The [endless-wave rule](../../docs/space-battle/REINFORCEMENTS.md) owns effect
timing and team selection; texture loading does not own simulation arrivals.

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
