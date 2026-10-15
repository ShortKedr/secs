[English](COVER.md) | Русский

# Обложка текущего геймплея

[Проект](../../README.ru.md) | [Отчёт](REPORT.ru.md) | [Обзор SpaceBattle](README.ru.md)

`cover.png` показывает текущий геймплей production AOT-веб-клиента в headless
Chromium. Захватывается только Canvas; HTML-панели управления и телеметрия
скрываются без изменения viewport и камеры. Это не
зафиксированный измеренный прогон, не бенчмарк и не доказательство
производительности в реальном времени. Исторические `action.png` и
`action.png.json`, их перечень хешей, записи parity и все файлы измерений
остаются неизменными.

Сохранённая пара обновлена 2026-10-14 из чистого локального checkout
`43316769acfcfa00a5307dd79490ef07e2f07ee6` через Chromium `151.0.7922.34`.
Захват записал `SourceDirty=false`, тик 180, 180 кораблей на экране и
722 снаряда; checksum боя — `79B277702009BA08`. Валидатор сверил
загруженные файлы с обслуживаемой AOT-сборкой, включая build identity,
манифест атласов и шесть сгенерированных текстур. Это подтверждает локальный
захват; удалённый workflow и развёртывание этим не проверены.

## Конфигурация и локальный захват

Источник параметров: [cover-settings.json](cover-settings.json), который читает
[cover.py](../../tools/space-battle/cover.py) и
[браузерный захват](../../tools/web/capture-cover.mjs). Для изменения сцены меняйте эту конфигурацию;
не поддерживайте второй набор констант захвата в документации или командах
workflow. Планируемый пресет: seed 7331, 96 кораблей сопровождения на команду, tick 180,
1600 x 1000 пикселей и zoom 2. Источник истины: файл настроек, а не это описание.
Профиль nominal; текущий веб-хост поддерживает ровно 96 кораблей сопровождения
на команду. Другие `ShipsPerTeam` отклоняются, а не молча игнорируются.

Локальное воспроизведение использует Python, Node.js, Playwright Chromium и
зафиксированный SDK .NET 8 с уже доступным workload `wasm-tools`. Соберите
тот же production AOT-веб-клиент в локальный каталог и используйте helper с JSON
настроек; это только захват, без серии бенчмарков или анализа отчёта. Кадр
выбирается по тику симуляции, а не прошедшему реальному времени: медленный
рендерер всё равно должен достичь заданного тика перед сохранением.
Фиксированные настройки делают сцену воспроизводимой; побайтовое совпадение
PNG между графическими драйверами не обещается.

Из корня репозитория (на Linux PowerShell-helper запускается через `pwsh`):

```bash
python -m pip install -r tools/space-battle/cover-requirements.txt
(cd tools/web && npm ci && npx playwright install chromium)
pwsh -File tools/build-space-battle-web.ps1 \
  -OutputDirectory Benchmarks.Local/tmp/cover-web -BasePath /
python tools/space-battle/cover.py capture \
  --site Benchmarks.Local/tmp/cover-web/wwwroot \
  --source-commit "$(git rev-parse HEAD)"
python tools/space-battle/cover.py validate \
  --directory Benchmarks.Local/tmp/space-battle-cover --allow-dirty
```

В Windows PowerShell запускайте build-helper напрямую, npm-команды из
`tools/web`, а хеш локального HEAD передайте строкой в `--source-commit`.
При проектном кеше браузера задавайте одинаковый `PLAYWRIGHT_BROWSERS_PATH`
для установки и захвата. Незакоммиченные локальные кадры помечаются `SourceDirty`;
`--allow-dirty` используется только при локальной проверке, никогда в Actions.
Захват пишет в `Benchmarks.Local/tmp/space-battle-cover`, а не в документы.

## Происхождение обложки

[cover.png.json](cover.png.json) фиксирует commit исходников, версию Chromium,
хеши загруженных файлов AOT-сборки, настройки, checksum симуляции и счётчики
рендера. Это данные конкретной обложки, а не результат бенчмарка.
