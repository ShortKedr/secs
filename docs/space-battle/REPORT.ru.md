[English](REPORT.md) | Русский

# Бенчмарки Space Battle — игровой high

Preset: `game`.

Актуальная кампания: 2026-10-15T02:39:29.9098413+05:00 — 2026-10-15T02:48:08.1498713+05:00. Профиль: `high`.

Текущая headless-симуляция измерена на пяти ECS: SECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs. Ревизии игровых систем: `mounted-armament-v7`, `layered-squads-v3`, `circle-kernel-v1`, `layered-weapon-damage-v1`.

## Ревизии фреймворков в замерах

Для сторонних ECS указаны точные коммиты исходников. Для SECS указан базовый коммит; хеши фактически измеренных исходников и DLL фиксируют манифест и JSON.

| Фреймворк | Ревизия исходников |
| --- | --- |
| SECS | `beca1e4c0277ca7f03b972a8d4ffef789b45ab19` |
| LeoECS Lite | `5053141d7d663c6b1855c8409ac93dfac807b239` |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` |
| StaticEcs | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` |

StaticEcs: [2.2.9 release](https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9); StaticPack source `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Как читать результаты

Единственный профиль `high` использует игровой preset со штатным `ChurnMultiplier=2`, seed 7331, 96 эскортами и 3 носителями на команду. Один и тот же сценарий выполняется на пяти ECS. Архивная кампания low/nominal/high использовала другой preset и игровую нагрузку; её время напрямую не сравнивается.

Игровой preset: флот LightSwarm, расстановка Tactical, EndlessReinforcements=true и ReplenishCarriers=false. Прогоны выполняются в Release, без tiered compilation и realtime-пейсинга.

[Архивная кампания конечного боя](REPORT_2026-10-14_FINITE.ru.md) сохраняет прежнюю методику и результаты.

Среднее полного прогона охватывает 108 000 измеряемых тиков после отдельного прогрева боя на 600 тиков. Desktop HUD Mean охватывает последние до 600 тиков и использует Battle.LastTickMs. Границы внешнего headless-таймера и таймеров HUD/стадий различаются; сравнивайте desktop и headless при одинаковых игровых настройках и диапазонах тиков.

Каждый случай — один независимый прогон из 108 000 тиков (30 минут симуляции), seed 7331, 60 тиков/с. Бюджет 16.67 ms относится к симуляции, а не к FPS. Квантили headless используют sorted[ceil((N−1)×p)]; live HUD — nearest rank ceil(N×p)−1. Один прогон на случай не даёт устойчивого рейтинга или доверительного интервала.

### Цветовая шкала

![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") до 5% · ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 15% · ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") от 30% · ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../benchmarks/COLORS.ru.md).

[Профиль Release-клиента и первые тики HUD](DESKTOP_TIMING.ru.md) отдельно проверяют холодный старт, tiering и paced replay.

## Проверка

Проверка пройдена: пять ECS-кейсов, 540 000 измеренных тиков; хеши и происхождение данных совпадают; игровые checksum, счётчики, тактика и трассы сущностей совпадают между портами. Census хранилища содержит только игровые сущности.

Локальные tick CSV проверены: время, стадии, HUD, аллокации и игровые счётчики.

## Время тика

Каждая цветная ячейка сравнивает пять ECS по одной метрике профиля high. Внешний тик включает игровые системы, физику и ECS commits; Managed B/тик не включает нативное хранилище ECS.

| Профиль | ECS | Среднее ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/тик | Всего внешних тиков s |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| high | SECS | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.8155 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.3228 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.6671 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 3.8497 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 601.6 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 88.071933 |
| high | LeoECSLite | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.1282 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.7972 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 2.2562 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 4.1613 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 681.0 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 121.850520 |
| high | LeoECS Classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 1.0081 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 1.5802 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 1.9417 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 3.9410 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 667.5 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 108.875424 |
| high | Morpeh | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8582 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.3634 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.7206 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 4.3154 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 655.8 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 92.688794 |
| high | StaticEcs 2.2.9 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8718 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 1.3969 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.7396 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 3.9212 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 619.2 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 94.154812 |

![Среднее время тика](current/MeanMs.png)

![P99 времени тика](current/P99Ms.png)

## Окна начала прогона

Окна 1–600 и 1–2 000 являются накопительными; остаток 2 001–108 000 их не перекрывает.

| Профиль | ECS | Накопительное среднее внешнее 1–600, ms | Накопительное среднее внешнее 1–2000, ms | Среднее внешнее 2001–108000, ms | Среднее внешнее 1–108000, ms |
| --- | --- | ---: | ---: | ---: | ---: |
| high | SECS | 0.8840 | 0.8005 | 0.8158 | 0.8155 |
| high | LeoECSLite | 1.0639 | 1.0381 | 1.1299 | 1.1282 |
| high | LeoECS Classic | 0.9986 | 0.9288 | 1.0096 | 1.0081 |
| high | Morpeh | 0.9450 | 0.8396 | 0.8586 | 0.8582 |
| high | StaticEcs 2.2.9 | 0.9531 | 0.8538 | 0.8721 | 0.8718 |

Первые 2 000 тиков дороже остатка у 0 из пяти ECS.

[Сводная CSV-таблица](current/summary.csv)

![Окна 1–600, 1–2 000, остаток и полный прогон](current/InitialWindows.png)

## Состояние симуляции и стадии

Состояние игры одинаково во всех пяти портах. Каждая стадия сравнивается между пятью ECS только в профиле high.

### Совпадение игрового состояния

| Профиль | Checksum | Кораблей | Турелей | Выстрелов | Попаданий | Создано | Уничтожено |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| high | 7EE0B6054181F4AE | 89 | 48 | 945589 | 728514 | 17469 | 17380 |

### Средние стадий за полный прогон

#### high

| Стадия | SECS, ms | Lite, ms | Classic, ms | Morpeh, ms | StaticEcs, ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2015 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.2546 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.2169 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1988 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.2093 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1364 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.1736 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.1614 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1353 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1398 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0123 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0194 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0164 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0130 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0176 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0307 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0399 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0367 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0317 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0347 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0265 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0364 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.0302 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0263 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0295 |
| carriers | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.0015 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0016 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0012 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0012 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0015 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0792 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1736 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1307 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1316 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1310 |
| hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.2808 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.3639 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.3501 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2774 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2665 |
| lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0465 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0653 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0645 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0428 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0418 |
| Battle.LastTickMs | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.8154 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.1281 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 1.0080 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8581 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8717 |
| Outer tick | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.8155 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.1282 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 1.0081 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8582 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8718 |

### Стадии SECS: начало, остаток и полный прогон

Стадии измерены внутри Battle.Step; внешний таймер тика включает также накладные расходы между стадиями и commit ECS.

#### high

| Стадия | 1–600 ms | 1–2000 ms | 2001–108000 ms | 1–108000 ms |
| --- | ---: | ---: | ---: | ---: |
| collect | 0.2140 | 0.2030 | 0.2015 | 0.2015 |
| ai | 0.1707 | 0.1387 | 0.1364 | 0.1364 |
| thermal-inventory | 0.0120 | 0.0107 | 0.0123 | 0.0123 |
| steering | 0.0353 | 0.0286 | 0.0307 | 0.0307 |
| weapons | 0.0321 | 0.0250 | 0.0265 | 0.0265 |
| carriers | 0.0005 | 0.0008 | 0.0015 | 0.0015 |
| physics | 0.0835 | 0.0729 | 0.0793 | 0.0792 |
| hits | 0.2870 | 0.2780 | 0.2808 | 0.2808 |
| lifecycle | 0.0488 | 0.0427 | 0.0466 | 0.0465 |
| Внешний тик | 0.8840 | 0.8005 | 0.8158 | 0.8155 |

## Окна HUD: по 600 тиков

Первое, второе и худшее окно сравнивают одну фазу всех пяти ECS. Худшее окно выбрано по максимальному среднему HUD-времени среди всех портов; границы у портов общие.

Среднее внутреннего HUD-таймера за все 108 000 тиков:

| Profile | HUD mean ms: SECS | Lite | Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: |
| high | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.8154 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.1281 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 1.0080 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8581 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.8717 |

### high

| Phase / ticks | HUD / stage mean ms | SECS | Lite | Classic | Morpeh | StaticEcs |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| first / 1–600 | HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.8839 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 1.0638 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.9984 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.9449 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.9530 |
| first / 1–600 | collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2140 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.2506 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.2255 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2246 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2182 |
| first / 1–600 | ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1707 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.1832 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.1796 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1692 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.1794 |
| first / 1–600 | thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0120 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0171 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0157 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.0137 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0188 |
| first / 1–600 | steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0353 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0415 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0397 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0374 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0419 |
| first / 1–600 | weapons | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0321 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.0378 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0326 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0304 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0364 |
| first / 1–600 | carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0005 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0004 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0003 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0006 |
| first / 1–600 | physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0835 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1597 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1262 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1379 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1367 |
| first / 1–600 | hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2870 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.3146 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.3170 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2867 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2813 |
| first / 1–600 | lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.0488 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0588 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0618 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0446 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0398 |
| second / 601–1200 | HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.0286 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.3429 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 1.1814 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.0434 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.0451 |
| second / 601–1200 | collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2629 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.3121 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2644 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2521 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2582 |
| second / 601–1200 | ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1789 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.2142 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.1928 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1732 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1751 |
| second / 601–1200 | thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0126 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0221 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0184 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0138 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0187 |
| second / 601–1200 | steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0344 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0405 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0372 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0365 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0376 |
| second / 601–1200 | weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0274 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0349 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0289 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0266 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0293 |
| second / 601–1200 | carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0010 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0012 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0008 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0007 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0010 |
| second / 601–1200 | physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0967 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.2148 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1513 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1565 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1533 |
| second / 601–1200 | hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.3571 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.4268 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.4087 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.3360 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.3242 |
| second / 601–1200 | lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0575 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0763 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0789 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0479 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0477 |
| worst / 92401–93000 | HUD total | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.0650 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 1.6350 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 1.1994 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.0833 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 1.0838 |
| worst / 92401–93000 | collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2526 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.3379 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2502 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2442 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2521 |
| worst / 92401–93000 | ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2032 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.2894 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.2185 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1971 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1990 |
| worst / 92401–93000 | thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0164 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0321 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.0203 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0183 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0221 |
| worst / 92401–93000 | steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0431 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0666 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0482 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0463 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0467 |
| worst / 92401–93000 | weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0321 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0525 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0336 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0324 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0337 |
| worst / 92401–93000 | carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0016 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0016 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0011 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0010 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0013 |
| worst / 92401–93000 | physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1051 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.2518 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1561 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1665 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.1626 |
| worst / 92401–93000 | hits | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.3523 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.5112 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.4006 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.3257 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.3166 |
| worst / 92401–93000 | lifecycle | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0586 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0918 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0708 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0517 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0497 |

| Phase / ticks | Mean ships | Mean projectiles | Mean debris | Mean particles | Mean turrets |
| --- | ---: | ---: | ---: | ---: | ---: |
| first / 1–600 | 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |
| second / 601–1200 | 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |
| worst / 92401–93000 | 108.08 | 880.04 | 1428.04 | 1240.26 | 44.83 |

Средняя численность совпадает во всех пяти ECS и показана один раз.

## Происхождение данных

- CPU: AMD Ryzen 9 9900X 12-Core Processor; RAM: 61.49 GiB.
- Runtime: .NET 8.0.32; OS: Microsoft Windows 10.0.26200; tiered compilation: `0`.
- Базовый commit: `beca1e4c0277ca7f03b972a8d4ffef789b45ab19`; хеши измеренных файлов отражают фактическое рабочее дерево и записаны в [current/manifest.json](current/manifest.json).
- [Опубликованные JSON и графики](current/); [сводная CSV-таблица](current/summary.csv); [оборудование](current/hardware.json). Локальные tick CSV остаются входом для проверки, их хеши записаны в манифесте.
- StaticEcs: [2.2.9 release](https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9), source `23b1cf36d89497e8a126d38459d688e2b0a5dea3`; StaticPack source `7bfb6cf925901b9366dda884258d727a89fff76e`.

Один прогон каждой ECS описывает эту машину и не даёт устойчивого рейтинга или доверительного интервала. Внешнее время тика включает игровые системы, физику и ECS commit; Battle.LastTickMs и таймеры стадий имеют более узкие границы. Рендеринг и snapshots/отчёт не входят. Managed allocation не включает нативное хранилище ECS.

## Воспроизведение

Из корня проекта с .NET 8, SDK с поддержкой C# 13, локальным кешем NuGet, Python 3.12 и matplotlib. Команда run-suite перезаписывает выбранную директорию результатов: для нового эксперимента укажите другую директорию внутри проекта. Анализатор проверяет данные и строит оба отчёта и графики.

```powershell
./tools/space-battle/run-suite.ps1 -Preset game -OutputDirectory docs/space-battle/current
python tools/space-battle/analyze_current.py --data-dir docs/space-battle/current --preset game --verify-ticks
```

Если локальные tick CSV недоступны, уберите --verify-ticks: анализатор проверит опубликованные JSON и хеши, но не повторит проверку CSV.

## История и связанные документы

- [Архив конечного боя 2026-10-14](REPORT_2026-10-14_FINITE.ru.md): прежняя матрица low/nominal/high, другая методика и игровой workload; время напрямую не сравнивается.
- [Архив измерений 2026-10-11](REPORT_2026-10-11.ru.md), [архитектура](ARCHITECTURE.ru.md), [физика](PHYSICS.ru.md), [тактика](TACTICS_V3.ru.md).
- [Микробенчмарки SECS](../../SECS.Benchmarks/README.ru.md): отдельная нагрузка без игровой логики.
