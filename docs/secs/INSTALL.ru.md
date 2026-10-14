[English](INSTALL.md) | Русский

# Установка SECS 0.1.0

SECS — ранняя ECS-библиотека для C#. После публикации скачайте файлы со
[страницы релиза `secs-v0.1.0`](https://github.com/ShortKedr/secs/releases/tag/secs-v0.1.0).
Пакет поставляется файлом `.nupkg` в GitHub Release; этот выпуск не публикует
его на NuGet.org. Сохраните пакет или Unity ZIP вместе с проектом, чтобы позднее
можно было восстановить зависимость.

## Приложения .NET

Используйте `ShortKedr.SECS.0.1.0.nupkg` из релиза как локальный источник
NuGet. Поместите его, например, в каталог `packages/` и создайте рядом с
проектом формата SDK файл `NuGet.Config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/>
  <add key="release" value="./packages" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
```

Затем добавьте пакет:

```powershell
dotnet add package ShortKedr.SECS --version 0.1.0
```

Используемая .NET Framework 4.8 сборка `netstandard2.0` зависит от
`System.Memory`. NuGet.org предоставляет эту зависимость, если её нет в
локальном офлайн-источнике.

Планируемые платформы первого выпуска — .NET 8 и .NET Framework 4.8 на
Windows. Успешная сборка библиотеки сама по себе не подтверждает запуск
приложения: перед релизом потребитель должен установить собранный пакет и
выполнить сценарий на обеих платформах. Более новые SDK могут выбирать
совместимую сборку, но в проверенную матрицу попадут только запущенные среды.

## Unity

Скачайте Unity ZIP из релиза. В проекте Unity 2022.3 LTS или Unity 6 выберите
профиль совместимости **.NET Standard 2.1**. Скопируйте все DLL из
`lib/netstandard2.1/` ZIP в `Assets/Plugins/` и дождитесь импорта. В ZIP также
будут лицензия MIT и эта инструкция. Подключите пространство имён `SECS` в
C#-скрипте. К опубликованному релизу должен прилагаться отчёт с точными
версиями Editor и проверенными backend. Базовые `World`,
`Query<T>().ForEach` и `Get<T>` не требуют
включения `unsafe` в проекте-потребителе. Для прямого доступа к указателям
чанков нужны `unsafe` и соответствующие настройки компиляции. Освобождайте
каждый `World`: он владеет нативной памятью.

Перед релизом нужно запустить тест импортированной DLL в Editor/Mono, а также
Windows Player с Mono и IL2CPP для каждой заявленной ветки Unity. Экспорты
WebGL и мобильные платформы в проверенную матрицу этого релиза не входят.

## Godot C#

Используйте Godot **4.5 .NET** на компьютере. Поместите `.nupkg` в локальный
источник и добавьте ссылку на пакет в C#-проект:

```xml
<ItemGroup>
  <PackageReference Include="ShortKedr.SECS" Version="0.1.0" />
</ItemGroup>
```

Используйте тот же `NuGet.Config` из раздела выше, чтобы восстановление
находило локальный пакет SECS и SDK Godot на NuGet.org.

```powershell
dotnet restore YourGodotProject.csproj --configfile NuGet.Config
```

Используйте `SECS` в C#-узле или другом управляемом классе. Перед релизом
нужно запустить сцену из [проверочного Godot-проекта](../../tools/release-consumers/Godot)
с установленным пакетом и получить успешное завершение. Экспорты Godot C# Web
и мобильные платформы здесь не проверяются.

## Минимальная проверка API

Проверочные проекты создают сущность, запускают запрос, читают изменённый
компонент и освобождают мир:

```csharp
using SECS;

using (var world = new World())
{
    var entity = world.CreateEntity(new Position { X = 2 });
    var processor = new Increment();
    world.Query<Position>().ForEach(ref processor);
    if (world.Get<Position>(entity).X != 3)
        throw new System.Exception("SECS query failed");
}

struct Position { public int X; }
struct Increment : IQueryProcessor<Position>
{
    public void Execute(Entity entity, ref Position position) => position.X++;
}
```

Компоненты SECS должны быть unmanaged-структурами. `World` не поддерживает
одновременный доступ из нескольких потоков; структурные изменения и
освобождение мира инвалидируют ссылки на компоненты и указатели чанков.
Подробности — в [руководстве по API](README.ru.md).

## Статус проверки

На 2026-10-15 локальный кандидат `.nupkg` прошёл независимую проверку
`CreateEntity → Query → Get → Dispose` на Windows x64:

| Потребитель | Локальный результат |
| --- | --- |
| .NET 8 (`net8.0`, SDK 8.0.426) | Пакет восстановлен из локального источника, приложение запущено. |
| .NET 10 (`net10.0`, SDK 10.0.204) | Приложение собрано и запущено именно для .NET 10 с совместимой сборкой пакета. |
| .NET Framework 4.8 (`net48`, SDK 8.0.426) | Windows-приложение запущено с пакетом и транзитивными зависимостями из локального офлайн-источника. |
| Godot 4.5 stable .NET, Windows x64 | Официальный редактор `4.5.stable.mono.official.876b29033` импортировал и запустил C#-сцену с пакетом. |
| Unity 2022.3.60f1 | Editor, Windows Mono Player и Windows IL2CPP Player прошли на отдельных копиях проверочного проекта. |
| Unity 6000.3.19f1 | Editor, Windows Mono Player и Windows IL2CPP Player прошли на отдельных копиях проверочного проекта. |

Скрипты [проверочных потребителей](../../tools/release-consumers) сверяют
SHA-256 установленного пакета с локальным кандидатом. Эти результаты не
доказывают, что будущий чистый релизный коммит даст те же файлы или что
опубликованные файлы скачаны и проверены. Окончательные заявления о поддержке
требуют точных версий и успешных логов в примечаниях к релизу. Пока все
обязательные проверки не прошли, это инструкция для готовящегося выпуска.
