[English](README.md) | Русский

# Библиотека SECS

Архетипная ECS для .NET 8 с тестами выполнения, микробенчмарками и автономным
примером космического сражения с видом сверху.

## Space Battle ECS Comparison

### [Читать отчёт о бенчмарках](docs/space-battle/REPORT.ru.md)

### [Запустить демо .NET WASM в браузере](https://shortkedr.github.io/secs/?lang=ru)

[Исходники Web и локальное воспроизведение](SpaceBattle.Web/README.ru.md). Браузер
выполняет общую симуляцию Core + SECS + Aether, а не полное сравнение пяти фреймворков.
Браузерные тайминги не являются результатами настольных бенчмарков;
равенство checksum между платформами не обещается.

[**Все реализации, бенчмарки и тесты**](Benchmarks.Local/README.ru.md) |
[**Сторонние лицензии и версии исходников**](THIRD_PARTY_NOTICES.ru.md)

Сравнение SECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs в одном и том же
автономном космическом сражении. Отчёт включает производительность симуляции
во времени, проверки, методику и воспроизведение. Результаты относятся к одной
машине и одному seed, а не определяют универсального победителя среди ECS.

[![Обложка текущего геймплея Space Battle](docs/space-battle/cover.png)](docs/space-battle/REPORT.ru.md)

Обложка: текущий геймплей, а не зафиксированный измеренный прогон. [Процесс захвата](docs/space-battle/COVER.ru.md).

[Методика бенчмарков](docs/space-battle/README.ru.md) |
[Системы симуляции](docs/space-battle/SYSTEMS.ru.md) |
[Историческая диагностика WASM](docs/space-battle/PERFORMANCE_INVESTIGATION.ru.md) |
[Интерактивный клиент](SpaceBattle.Game/README.ru.md)

### Space Battle

Большие корабли получили [независимые турели и тяжёлую пушку корпуса](docs/space-battle/ARMAMENT.ru.md).
Текущий геймплей новее зафиксированной нагрузки бенчмарка.

Пример объединяет управление движением через AI, одиннадцать классов кораблей,
пять типов оружия, составные коллизионные тела, корпус и щиты, инвентари,
управление температурой, производство, стыковку и ремонт на авианосцах,
а также инерционные обломки, наносящие урон. AI рассчитывает упреждение по
скорости; 25% решений о выстреле имеют детерминированный разброс до +/-0.03 радиана.
Снаряды и обломки нейтральны к командам и могут поразить любую сторону,
включая стрелявший корабль. SECS управляет клиентом Silk.NET; headless-хост
также поддерживает LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs 1.2.01 через
адаптеры нативного хранения и запросов.

- [План реализации и приёмки](docs/space-battle/PLAN.ru.md)
- [Отчёт о симуляции во времени](docs/space-battle/REPORT.ru.md)
- [Методика бенчмарков и воспроизведение](docs/space-battle/README.ru.md)
- [Управление клиентом и снимки](SpaceBattle.Game/README.ru.md)
- [Сгенерированные корабли и промпт](SpaceBattle.Game/Assets/PROVENANCE.ru.md)
- [Контракты и проверка нативных адаптеров](SpaceBattle.Adapters/README.ru.md)
- [Существующие микробенчмарки пяти фреймворков](SECS.Benchmarks/README.ru.md)

## Сборка и Запуск

Из корня репозитория сохраняйте локальные кеши инструментов и пакетов внутри проекта:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
dotnet restore SpaceBattle.Game/SpaceBattle.Game.csproj
dotnet build SpaceBattle.Game/SpaceBattle.Game.csproj -c Release --no-restore -m:1
dotnet SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.dll
```

Для остальных четырёх ECS-адаптеров закреплённые исходники находятся в
`Benchmarks.Local/vendor` и включены в репозиторий с оригинальными лицензиями.
`Benchmarks.Local/fetch.ps1` нужен только для проверяемого восстановления,
а не для обычного клона. У LeoECS условная MIT-Red; см.
[перечень лицензий](THIRD_PARTY_NOTICES.ru.md). Headless-запуски не инициализируют
OpenGL и не загружают сгенерированные спрайты:

```powershell
dotnet restore SpaceBattle.Headless/SpaceBattle.Headless.csproj
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1
$env:DOTNET_TieredCompilation = '0'
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --framework secs --seconds 1800 --output docs/space-battle/custom/custom-secs.json
```

При воспроизведении строгого набора из 15 случаев используйте отдельный каталог
вывода для собственных запусков. Анализатор ожидает только фиксированную кампанию
low/nominal/high в `docs/space-battle/results`.
