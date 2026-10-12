[English](README.md) | Русский

<p align="center">
  <img src="docs/branding/shortkedr-ecs-emblem.png" width="200" alt="ShortKedr ECS — белка как продолжение кедровой шишки">
</p>

# Библиотека SECS

Архетипная ECS для .NET 8 с тестами выполнения, микробенчмарками и автономным
примером космического сражения с видом сверху. **SECS** — **ShortKedr ECS**.

[Быстрый старт](#быстрый-старт) · [Space Battle](#space-battle) ·
[Бенчмарки](#бенчмарки) · [Документация](#документация)

## Библиотека

- **Архетипное хранение:** unmanaged-компоненты сгруппированы в чанки настраиваемого размера.
- **Типизированные запросы:** обновление компонентов по `ref` через `IQueryProcessor` или прямой обход чанков.
- **Жизненный цикл сущностей:** создание и уничтожение; добавление, чтение, обновление и удаление компонентов.
- **Стадии систем:** регистрация работы в `PreUpdate`, `Update` и `PostUpdate`.

## Быстрый старт

Нужен .NET SDK с поддержкой .NET 8. Запускайте из корня репозитория; эти настройки
сохраняют кеши инструментов и пакетов внутри проекта:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"

dotnet run --project SECS.Sample/SECS.Sample.csproj -c Release
dotnet test SECS.Tests/SECS.Tests.csproj -c Release
```

Для использования библиотеки в своём приложении .NET 8 добавьте ссылку на проект
[SECS.csproj](SECS/SECS.csproj). [Исходник примера](SECS.Sample/Program.cs)
показывает работу с сущностями, запросами, системами и обходом чанков.

<details>
<summary>Минимальный типизированный запрос</summary>

```csharp
using SECS;

using var world = new World();
var entity = world.CreateEntity();
world.Add(entity, new Position { X = 0, Y = 0 });
world.Add(entity, new Velocity { X = 3, Y = 1 });

var movement = new Movement();
world.Query<Position, Velocity>().ForEach(ref movement);

var position = world.Get<Position>(entity);
Console.WriteLine($"Position: ({position.X}, {position.Y})");

struct Position { public float X, Y; }
struct Velocity { public float X, Y; }

struct Movement : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}
```

</details>

## Space Battle

**[Открыть демо в браузере](https://shortkedr.github.io/secs/?lang=ru)** ·
**[Читать отчёт о бенчмарках](docs/space-battle/REPORT.ru.md)** ·
[Управление клиентом](SpaceBattle.Game/README.ru.md)

Автономные флоты объединяют управление через AI, стрельбу с упреждением, турели,
щиты, производство, стыковку и ремонт на авианосцах. Клиент Silk.NET работает
на SECS; headless-хост также поддерживает LeoECS Lite, LeoECS Classic, Morpeh
и StaticEcs через адаптеры нативного хранения и запросов.

[![Текущий геймплей Space Battle](docs/space-battle/cover.png)](docs/space-battle/REPORT.ru.md)

*Текущий геймплей новее зафиксированной нагрузки бенчмарка.
[Процесс захвата](docs/space-battle/COVER.ru.md).*

Браузер выполняет общую симуляцию с SECS; его тайминги отличаются
от настольного бенчмарка. Равенство checksum между платформами не обещается.
См. [исходники Web и воспроизведение](SpaceBattle.Web/README.ru.md).

### Локальный запуск

Используйте настройки кешей из быстрого старта. Клиенту нужно окружение
с поддержкой OpenGL 3.3; headless-хост не инициализирует OpenGL
и не загружает сгенерированные спрайты.

<details>
<summary>Интерактивный клиент</summary>

```powershell
dotnet restore SpaceBattle.Game/SpaceBattle.Game.csproj
dotnet build SpaceBattle.Game/SpaceBattle.Game.csproj -c Release --no-restore -m:1
dotnet SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.dll
```

</details>

<details>
<summary>Headless-симуляция</summary>

```powershell
dotnet restore SpaceBattle.Headless/SpaceBattle.Headless.csproj
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1
$env:DOTNET_TieredCompilation = '0'
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --framework secs --seconds 1800 --output docs/space-battle/custom/custom-secs.json
```

Сохраняйте собственные результаты вне `docs/space-battle/results`: строгий
анализатор 15 случаев ожидает там только фиксированную кампанию low/nominal/high.

</details>

## Бенчмарки

| Нагрузка | Результаты и воспроизведение |
| --- | --- |
| Создание, обход, изменение состава и уничтожение сущностей | [Микробенчмарки](SECS.Benchmarks/README.ru.md) |
| Автономное сражение во времени | [Отчёт Space Battle](docs/space-battle/REPORT.ru.md) |
| Настройка и проверка пяти фреймворков | [Локальный сравнительный прогон](Benchmarks.Local/README.ru.md) |

Сохранённые результаты описывают измеренную машину, runtime, версии фреймворков
и нагрузки. Они не определяют универсального победителя среди ECS. Локальное
сравнение пяти фреймворков использует Stopwatch, а набор только для SECS —
BenchmarkDotNet. Методика и исходные результаты приведены в соответствующих документах.

## Документация

| Тема | Руководство |
| --- | --- |
| Производительность библиотеки | [Оптимизации SECS](docs/SECS_OPTIMIZATION.ru.md) |
| Детерминизм | [План тестов и GitHub Actions](docs/SECS_DETERMINISM_PLAN.ru.md) — план реализации |
| Устройство сражения | [План реализации](docs/space-battle/PLAN.ru.md) · [Системы симуляции](docs/space-battle/SYSTEMS.ru.md) · [Вооружение](docs/space-battle/ARMAMENT.ru.md) |
| ECS-адаптеры | [Контракты и проверка](SpaceBattle.Adapters/README.ru.md) |
| Графика и снимки | [Ассеты кораблей и промпты](SpaceBattle.Game/Assets/PROVENANCE.ru.md) · [Обложка](docs/space-battle/COVER.ru.md) |
| Производительность браузера | [Историческая диагностика WASM](docs/space-battle/PERFORMANCE_INVESTIGATION.ru.md) |
| Сторонний код | [Лицензии и закреплённые исходники](THIRD_PARTY_NOTICES.ru.md) |

Закреплённые исходники остальных четырёх ECS-адаптеров включены в
`Benchmarks.Local/vendor` вместе с оригинальными лицензиями. `Benchmarks.Local/fetch.ps1`
нужен только для восстановления. У LeoECS условная MIT-Red; перед повторным
использованием сверяйтесь с перечнем лицензий.
