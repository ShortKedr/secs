[English](THIRD_PARTY_NOTICES.md) | Русский

# Сторонние Исходники и Лицензии

[Репозиторий](README.ru.md) | [Все реализации и тесты](Benchmarks.Local/README.ru.md) | [Отчёт о бенчмарках](docs/space-battle/REPORT.ru.md)

Репозиторий сравнивает пять реализаций ECS на общих нагрузках. Сторонний код
остаётся работой соответствующих авторов. Сравнение, исследование или упоминание
фреймворка не отменяет условий лицензии и не означает одобрения авторами.
Этот перечень фиксирует проверенные лицензии исходников, но не гарантирует
соответствия всем законам, юрисдикциям, патентам или обстоятельствам пользователя.

## Закреплённые Исходники

Распространяются только указанные ниже части исходников выполнения и их точные
оригинальные лицензии. Это обычные файлы репозитория, а не подмодули, внешние
ссылки вместо кода или обязательная загрузка. Все включённые сторонние файлы
побайтово совпадают с архивами закреплённых коммитов; уведомления об авторстве сохранены.

| Компонент | Авторы / copyright в лицензии | Условия | Включённые исходники | Точная лицензия |
| --- | --- | --- | --- | --- |
| LeoECS Classic | 2012 - 2023 leopotam@yandex.ru | MIT-Red либо отдельно согласованная собственническая/коммерческая лицензия | [ecs/src](Benchmarks.Local/vendor/ecs/src/) | [Оригинал](Benchmarks.Local/vendor/ecs/LICENSE.md), [закреплённый upstream](https://github.com/Leopotam/ecs/blob/1213c31c9ef8325d48e74a52b345c4db242d038b/LICENSE.md) |
| LeoECS Lite | 2012-2024 leopotam@yandex.ru | MIT-Red | [ecslite/src](Benchmarks.Local/vendor/ecslite/src/) | [Оригинал](Benchmarks.Local/vendor/ecslite/LICENSE.md), [закреплённый upstream](https://github.com/Leopotam/ecslite/blob/5053141d7d663c6b1855c8409ac93dfac807b239/LICENSE.md) |
| Morpeh | 2025 Oleg Morozov, Igor Boyko, Matvey Reschikov | MIT | [Morpeh Core](Benchmarks.Local/vendor/morpeh/Scellecs.Morpeh/Core/) | [Оригинал](Benchmarks.Local/vendor/morpeh/LICENSE.md), [закреплённый upstream](https://github.com/scellecs/morpeh/blob/9f6369384792a01d74df24efd35b08ae313e4f12/LICENSE.md) |
| StaticEcs | 2024 FFS | MIT | [StaticEcs/Src](Benchmarks.Local/vendor/StaticEcs/Src/) | [Оригинал](Benchmarks.Local/vendor/StaticEcs/LICENSE.md), [закреплённый upstream](https://github.com/Felid-Force-Studios/StaticEcs/blob/6323d710c22cfff70100fe3748de432cb5fc70c8/LICENSE.md) |
| StaticPack | 2025 FFS | MIT | [StaticPack/Src](Benchmarks.Local/vendor/StaticPack/Src/) | [Оригинал](Benchmarks.Local/vendor/StaticPack/LICENSE.md), [закреплённый upstream](https://github.com/Felid-Force-Studios/StaticPack/blob/72ce8b993593e5cfd24c6a9289a4b132305ef493/LICENSE.md) |

StaticPack является зависимостью StaticEcs, а не отдельной измеряемой ECS. Точные
URL репозиториев, полные commit ID, размеры файлов и SHA-256 записаны в
[манифесте исходников](Benchmarks.Local/vendor-manifest.json).

## Условия Лицензий

**MIT:** сохраняйте уведомления об авторском праве и разрешении каждого автора
во всех копиях или значимых частях, в том числе в отдельно распространяемых
пакетах исходников/бинарных файлов. Исходные отказы от гарантий и ответственности
сохраняют силу. Не удаляйте и не заменяйте эти уведомления лицензией собственного кода.

**MIT-Red не является обычной MIT.** Закреплённые лицензии LeoECS содержат
дополнительные условия, касающиеся поддержки применения открытого ПО в военной
сфере, а также ненависти или поддержки нападок на русских. Они запрещают
использование, если хотя бы одно условие невыполнимо, и требуют сохранять copyright
и полный текст условий в копиях или значимых частях. Определяющим остаётся
оригинальный русский текст; это краткое описание не заменяет его и не является
юридической интерпретацией. Пользователи и распространители должны оценить,
соответствуют ли они условиям. Если нет, использовать покрываемый код по MIT-Red
нельзя. Для Classic также описаны индивидуально согласуемые собственнические/
коммерческие условия; наличие такого разрешения здесь не заявляется. Нельзя
обозначать весь сравнительный набор как неограниченный или целиком MIT.
При необходимости уточняйте условия у правообладателя.

## Наши Адаптации

Сторонние исходники и лицензии не изменены. Наши [адаптеры микробенчмарков](Benchmarks.Local/Adapters.cs),
[статические адаптеры](Benchmarks.Local/StaticAdapters.cs), [стресс-адаптеры](Benchmarks.Local/StressAdapters.cs)
и [хранилища SpaceBattle](SpaceBattle.Adapters/) являются отдельными сравнительными
интеграциями, а не реализациями upstream или одобренными авторами рекомендациями
по производительности.

[StaticLibrary.csproj](Benchmarks.Local/StaticLibrary.csproj) собирает исходники
StaticEcs и StaticPack для .NET 8. [MorpehLibrary.csproj](Benchmarks.Local/MorpehLibrary.csproj)
собирает неизменённый Morpeh Core для netstandard2.1 с исходной целевой платформой
и Debug defines, без ресурсов Unity/пакета/редактора и без генерации NuGet-пакета.
Это наши сборочные обёртки, а не изменения алгоритмов ECS.
Сборка после упаковки может иметь другие хеши бинарных файлов, чем исторические
измерения; сохранённый provenance бенчмарков не переписывается ради обратного утверждения.

## Другие Зависимости

NuGet-зависимости объявлены в проектах и восстанавливаются обычным способом; их
кеши пакетов, бинарные файлы, SDK и нативные библиотеки **не распространяются в
этом репозитории исходников**. Среди них [Aether.Physics2D 2.2.0](https://www.nuget.org/packages/Aether.Physics2D/2.2.0)
(MS-PL), [Silk.NET 2.23.0](https://www.nuget.org/packages/Silk.NET.Windowing/2.23.0),
[StbImageSharp 2.30.16](https://www.nuget.org/packages/StbImageSharp/2.30.16),
[BenchmarkDotNet 0.14.0](https://www.nuget.org/packages/BenchmarkDotNet/0.14.0)
и инструменты xUnit/test SDK/coverage. Это не полный перечень транзитивных
лицензий бинарной поставки. Будущим исполняемым файлам, NuGet-пакетам или архивам
релиза нужна отдельная проверка зависимостей/лицензий и обязательных уведомлений
для всего включённого. У сгенерированных изображений клиента есть отдельный
[provenance](SpaceBattle.Game/Assets/PROVENANCE.ru.md).

Здесь не назначается новая лицензия SECS, нашим адаптерам, тестам, документации,
данным или изображениям. Лицензию публикации выбирает правообладатель;
отсутствие корневой лицензии не означает public domain или MIT.
Сторонние условия выше относятся только к покрываемому ими коду.

## Браузерная Поставка

[WebAssembly-демо](SpaceBattle.Web/README.ru.md) распространяет собранные SECS,
SpaceBattle.Core и SpaceBattle.SECS с Aether.Physics2D 2.2.0 (MS-PL),
.NET/Blazor 8.0.31 (MIT и исходные уведомления о сторонних компонентах) и Lucide 1.55.0
(ISC / MIT для иконок на основе Feather). Статический сайт содержит оригинальные
лицензии, copyright и provenance изображений в
[каталоге лицензий](SpaceBattle.Web/wwwroot/licenses/), включая
[состав поставки](SpaceBattle.Web/wwwroot/licenses/NOTICE.txt).
Production AOT-поставка также сохраняет [лицензию Emscripten 3.1.34](SpaceBattle.Web/wwwroot/licenses/EMSCRIPTEN.txt)
и [список авторов](SpaceBattle.Web/wwwroot/licenses/EMSCRIPTEN-AUTHORS.txt) из
workload .NET WebAssembly. Бинарные файлы компилятора и toolchain остаются
локальными инструментами сборки, а не ресурсами сайта.
В демо не включены четыре сравниваемые ECS-библиотеки, зависимости нативного
OpenGL и зависимости бенчмарков/тестов. Условия лицензирования сравнительного
набора исходников выше не меняются.

## Проверка

```powershell
python tools/check_vendor.py
python -m unittest discover -s tools -p 'test_check_vendor.py' -v
python tools/check_docs.py
```

Проверка контролирует хеши, наличие лицензий, точный регистр путей и совпадение
публикуемого Git набора исходников с манифестом. [.gitattributes](.gitattributes)
предотвращает преобразование переводов строк в проверяемых сторонних файлах.
[fetch.ps1](Benchmarks.Local/fetch.ps1) позволяет восстановить отсутствующие файлы
манифеста из закреплённых архивов, проверяет хеши до копирования и отказывается
перезаписывать изменённые локальные исходники. Обычному клону этот шаг не нужен.
