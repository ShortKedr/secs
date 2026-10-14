[English](INSTALL.md) | Русский

# Установка локально собранного кандидата SECS 0.1.0

SECS — ранняя ECS-библиотека для C#. GitHub Release `secs-v0.1.0` пока не
опубликован. Из корня репозитория соберите локальный пакет:

```powershell
dotnet pack SECS/SECS.csproj -c Release -o artifacts/release
```

Полученный `ShortKedr.SECS.0.1.0.nupkg` — локальный кандидат. Эта инструкция
использует локальный источник пакетов. Сохраните копию вместе с проектом для
повторяемого восстановления.

## Приложения .NET

Используйте локально собранный `ShortKedr.SECS.0.1.0.nupkg` как источник
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

Потребители с установленным пакетом запускались на .NET 8, .NET 10 и
.NET Framework 4.8 под Windows. Одна лишь сборка библиотеки не подтверждает
работу приложения; наблюдаемые локальные результаты перечислены ниже.

## Unity

Локальный [скрипт создания Unity ZIP](../../tools/release/make_unity_zip.py)
упаковывает те же DLL, что и `.nupkg`. В проекте Unity 2022.3 LTS или Unity 6 выберите
профиль совместимости **.NET Standard 2.1**. Скопируйте все DLL из
`lib/netstandard2.1/` ZIP в `Assets/Plugins/` и дождитесь импорта. В ZIP также
входят лицензия MIT и эта инструкция. Подключите пространство имён `SECS` в
C#-скрипте. Базовые `World`,
`Query<T>().ForEach` и `Get<T>` не требуют
включения `unsafe` в проекте-потребителе. Для прямого доступа к указателям
чанков нужны `unsafe` и соответствующие настройки компиляции. Освобождайте
каждый `World`: он владеет нативной памятью.

Перечисленные ниже локальные проверки запускали импортированную DLL в Editor
и Windows Player с Mono и IL2CPP. Экспорты WebGL и мобильных платформ не проверялись.

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

Используйте `SECS` в C#-узле или другом управляемом классе. Локальный
[проверочный Godot-проект](../../tools/release-consumers/Godot) импортировал
и запустил сцену с установленным пакетом. Экспорты Godot C# Web и мобильных
платформ не проверялись.

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
SHA-256 установленного пакета с локальным кандидатом. Эти запуски использовали
локальный кандидат из рабочего дерева. Они не подтверждают состав или работу
какого-либо опубликованного релиза.
