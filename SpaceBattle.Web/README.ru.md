[English](README.md) | Русский

# SpaceBattle.Web

**[Запустить демо](https://shortkedr.github.io/secs/?lang=ru)** |
[Отчёт о бенчмарках](../docs/space-battle/REPORT.ru.md) |
[Репозиторий](../README.ru.md)

## Среда Выполнения и Сценарий

Это настоящий хост .NET 8 Blazor WebAssembly для неизменённой симуляции
[SpaceBattle.Core](../SpaceBattle.Core), [SpaceBattle.SECS](../SpaceBattle.SECS)
и [SECS](../SECS) с Aether.Physics2D. Симуляция на C# выполняется в браузере;
JavaScript Canvas служит тонким рендерером, а не переписанной на JavaScript
версией боя. Исходники Web-хоста и рендерера находятся в этом каталоге.

Три существующих churn-профиля: low=0.5, nominal=1 и high=2,
96 начальных эскортов и 3 авианосца на команду. Общий Core сохраняет
прицеливание с упреждением, детерминированный разброс и нейтральный к командам
урон снарядов и обломков, включая попадания по своим и в себя. Подробности
правил описаны в [системах симуляции](../docs/space-battle/SYSTEMS.ru.md).

## Публикация и Локальный Запуск

Из корня репозитория используйте .NET SDK 8.0.425 (или более новый патч 8.0.4xx,
выбираемый через [global.json](global.json)) и
[скрипт публикации](../tools/build-space-battle-web.ps1), выполняющий `dotnet publish`.
По умолчанию `-OutputDirectory` равен `Benchmarks.Local/tmp/web-publish`,
а `-BasePath` равен `/secs/`: это путь публикации на GitHub Pages.

```powershell
./tools/build-space-battle-web.ps1
```

Для локального статического сервера публикуйте с `-BasePath /`, чтобы ресурсы
загружались от корня сервера. При установленном Python запустите сервер
в опубликованном каталоге `wwwroot`:

```powershell
./tools/build-space-battle-web.ps1 -OutputDirectory Benchmarks.Local/tmp/web-publish -BasePath /
Set-Location Benchmarks.Local/tmp/web-publish/wwwroot
python -m http.server 8000
```

Откройте [локальное демо](http://localhost:8000/) или
[его русский интерфейс](http://localhost:8000/?lang=ru). Для Pages повторите
публикацию с базовым путём `/secs/` по умолчанию; это не корневой путь локального сервера.

## GitHub Pages

[Workflow GitHub Actions](../.github/workflows/space-battle-pages.yml)
собирает и развёртывает статическое демо по
[целевому адресу Pages `/secs/`](https://shortkedr.github.io/secs/).
Демо содержит обратные ссылки на двуязычный отчёт о бенчмарках. Русский адрес демо:
[`?lang=ru`](https://shortkedr.github.io/secs/?lang=ru).
Наличие целевой ссылки само по себе не подтверждает завершение развёртывания.

Перед деплоем Actions выполняет реальные проверки в Chromium и сохраняет
desktop/mobile-скриншоты в артефакте `space-battle-browser-checks`. Проверяются
все профили, повторяемость сброса внутри браузера, счётчики боя, камера/выделение,
touch, адаптивная раскладка и восстановление после ошибки. Локальный запуск:

```powershell
./tools/build-space-battle-web.ps1
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/Benchmarks.Local/tmp/playwright-browsers"
npm ci --prefix tools/web --cache Benchmarks.Local/tmp/npm-cache
node tools/web/node_modules/playwright/cli.js install chromium
node tools/web/check-web.mjs --base /secs/
```

В опубликованном сайте есть [состав лицензионной поставки](wwwroot/licenses/NOTICE.txt)
и оригинальные сторонние уведомления в `wwwroot/licenses/`.

## Метрики и Воспроизведение

Демо выполняет только SECS; это не полное сравнение пяти фреймворков.
Браузерные тайминги характеризуют текущий браузер, среду WebAssembly,
устройство и условия выполнения. Рендеринг, interop, прогрев и планирование
вкладок могут влиять на работу; браузерные показатели не взаимозаменяемы
с измеренными настольными headless Step или отдельным профилем OpenGL-фронтенда.
Равенство checksum между платформами и гарантированный realtime FPS не обещаются.

Измеренное сравнение и его ограничения приведены в
[отчёте](../docs/space-battle/REPORT.ru.md). Для воспроизведения настольной кампании
следуйте [методике бенчмарков](../docs/space-battle/README.ru.md) и
[перечню реализаций и сборок](../Benchmarks.Local/README.ru.md).
Публикация или запуск демо не пересоздаёт и не заменяет эти измерения.
