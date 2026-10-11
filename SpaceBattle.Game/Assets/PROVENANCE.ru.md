[English](PROVENANCE.md) | Русский

# Сгенерированный Атлас Кораблей

[Клиент](../README.ru.md) |
[Отчёт о бенчмарках](../../docs/space-battle/REPORT.ru.md) |
[Обзор репозитория](../../README.ru.md)

`ships-atlas.png` сгенерирован встроенным инструментом `image_gen.imagegen`
с `transparent_background=true` для этого проекта. Это не скачанный сторонний
набор кораблей. Исходное изображение остаётся неизменным. Полученный PNG имеет
размер 1448 x 1086; интервалы генератора были неравномерными, поэтому рендерер
использует измеренные пиксельные прямоугольники из `ships-atlas.json`, сохраняя
пропорции каждого спрайта. Очень слабые остатки альфа-канала отбрасываются
шейдером без изменения PNG.

Классы 0-7 являются кораблями сопровождения, 8-10 авианосцами, а 11 является
неиспользуемым спрайтом шаттла. Командная окраска, щиты, следы двигателей,
тепловые контуры и эффекты рисуются во время выполнения. Атлас не содержит
HUD или заранее нарисованной сцены сражения.

## Промпт Генерации

Исходный промпт приведён без перевода и изменений:

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
