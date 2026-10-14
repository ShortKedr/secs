[English](RESEARCH.md) | Русский

# Исследования SpaceBattle

[Текущий геймплей и запуск](README.ru.md) | [Текущая измеренная кампания](REPORT.ru.md)

Читайте каждое исследование с учётом записанной ревизии исходников, сценария и среды исполнения. Последующие изменения игры не обновляют эти замеры. Совпадение результатов до и после изменений внутри одной среды не доказывает кроссплатформенный детерминизм. Время отдельных стадий описывает части симуляции, а не FPS полного клиента.

## Архивная кампания и проектирование

| Документ | Контекст |
| --- | --- |
| [Отчёт 2026-10-11](REPORT_2026-10-11.ru.md) | Пять ECS, `predictive-neutral-v1`, Aether; исходные таблицы, графики и кадры |
| [Каталог систем](SYSTEMS.ru.md) | Исходные девять стадий и компоненты; исторический каталог, а не текущая инвентаризация кода |
| [План поставки 2026-10-11](PLAN.ru.md) | Первоначальные требования и журнал приёмки; не актуальная очередь работ |
| [Тактический ИИ v2](TACTICS.ru.md) | Прежняя модель стоимости убийства и фокус-групп, заменена [v3](TACTICS_V3.ru.md) |

## Исторические визуальные свидетельства

Эти пары PNG/sidecar — сохранённые исторические снимки, а не результаты текущего захвата. Их воспроизведение из текущих исходников не проверено.

| Снимок | PNG | Записанные метаданные |
| --- | --- | --- |
| Tick 0 | [tick-0.png](tick-0.png) | [sidecar](tick-0.png.json) |
| Tick 900 | [tick-900.png](tick-900.png) | [sidecar](tick-900.png.json) |
| Tick 900, 960 x 540 | [tick-900-960x540.png](tick-900-960x540.png) | [sidecar](tick-900-960x540.png.json) |
| Tick 3600 | [tick-3600.png](tick-3600.png) | [sidecar](tick-3600.png.json) |
| Stress 4102, tick 0 | [stress-4102-tick-0.png](stress-4102-tick-0.png) | [sidecar](stress-4102-tick-0.png.json) |

## Производительность и совместимость

| Исследование | Дата / область | Инструменты воспроизведения |
| --- | --- | --- |
| [Расследование производительности](PERFORMANCE_INVESTIGATION.ru.md) | 2026-10-11; SECS native/WASM до прикреплённых орудий | [Performance tools](../../tools/performance/README.ru.md) |
| [Пространственная оптимизация](SPATIAL_OPTIMIZATION.ru.md) | 2026-10-12; before/after Aether с орудиями, до кругового ядра | [Performance tools](../../tools/performance/README.ru.md) |
| [Аппаратная оптимизация](HARDWARE_OPTIMIZATION.ru.md) | 2026-10-12; LightSwarm/круги, native и Mono AOT | [Hardware harness](../../tools/HardwareStudy/README.ru.md) |
| [Переносимая локальность](../../tools/HardwareStudy/LOCALITY_OPTIMIZATION.ru.md) | 2026-10-12; component-only движение и ленивые инварианты сохранённых исходников | [Hardware harness](../../tools/HardwareStudy/README.ru.md) |
| [Компилятор Mono](../../tools/web/MONO_COMPILER_STUDY.ru.md) | 2026-10-12; O3/LTO относительно записанного Mono AOT baseline | Команды и замороженные manifests в исследовании |
| [Круговая физика](PHYSICS.ru.md) | Текущая модель и протокол сравнения с сохранённым Aether | [Physics harness](../../tools/physics/README.ru.md) |
| [Структурная оптимизация ядра](../../tools/physics/KERNEL_OPTIMIZATION.ru.md) | Circle-vs-circle на заданных состояниях; отдельно от Aether | Команды в исследовании |
| [Расследование iOS](IOS_WEB.ru.md) | Снимок 2026-10-12; восстановление на настоящем iPhone не проверено | Ограниченные браузерные проверки в руководстве |

Для снимков используйте [обложку](COVER.ru.md), для видео — [поиск сцен](../../tools/space-battle/scenes/README.ru.md). Эти процессы не измеряют throughput. Новое исследование требует свежих локальных outputs, сохранённого baseline и свободного окна измерений.
