# Исследование SpaceGame для GA SECS

Дата: 12 октября 2026 года. Статус: исследование текущих исходников и уточнение требований. SpaceGame в этом документе означает пример игры из проектов SpaceBattle.Core, SpaceBattle.SECS, SpaceBattle.Game, SpaceBattle.Headless и SpaceBattle.Web. Изменения игровой реализации не входят в это исследование.

Главный вывод: пример подтверждает пригодность архитектуры ECS и фазового исполнения, но общего тела Execute с двумя компонентами недостаточно для реальной игры. GA нужны объявленные дополнительные доступы, tick resources, snapshot buffers, lookup с поколением, доменный порядок событий и общая модель упорядоченных CPU стадий. Эти возможности должны сохранять игровые правила на обоих исполнителях.

Обновлены [ТЗ](SPEC.ru.md), [план реализации](IMPLEMENTATION_PLAN.ru.md) и [guideline](GUIDELINES.ru.md). Исследование не является проверкой GPU исполнения или новым бенчмарком: критерии таких проверок включены в план, а текущие проверки игры рассмотрены по их исходникам.

## Реальная граница игры и SECS

[Battle](../../../SpaceBattle.Core/Battle.cs) содержит правила игры и оркестрацию. [IEcsStore](../../../SpaceBattle.Core/EcsContract.cs) предоставляет синхронные Create/Destroy/Get/Set/Has/Remove/Commit и ForEach на 1–3 компонента. [SecsStore](../../../SpaceBattle.SECS/SecsStore.cs) переводит EntityKey в SECS.Entity, кеширует queries и вызывает CPU processors через Bridge. Текущий QueryKey дополнительно имеет bounded reverse cache native ID/version → public EntityKey с invalidation на create/destroy и fallback для больших IDs. Это оптимизация identity translation, не GPU execution или tracking всех записей компонентов. Фреймворк не знает полный набор доступов системы через этот интерфейс.

[NativeStore](../../../SpaceBattle.Core/NativeStore.cs), строки 29–36, запрещает структурные изменения внутри query, но Get возвращает обычный живой ref. Create/Destroy назначают публичные ID и поколения сразу, отдельно от native handles. Следовательно, замена SecsStore на GPU store не переводит весь Battle.Step в GPU или async исполнение.

Многие processors содержат поле Battle<TStore> и вызывают его методы. Inventory, Steering и Dock изменяют больше компонентов своей сущности, чем передано в Execute. Weapon и ProjectileMotion читают target/home/parent. Thermal и AI пишут общие request lists и counters. Конкретное основание: [Battle.cs](../../../SpaceBattle.Core/Battle.cs), строки 196–290, [CombatSystems.cs](../../../SpaceBattle.Core/CombatSystems.cs), строки 9–164.

Для portable варианта требуется механическое выражение этих же доступов через объявленный контекст, без managed closure в kernel. Один source остаётся общим для CPU/GPU. Текущий legacy Battle продолжает работать через свой store; автоматической совместимости старого callback с GA не утверждается.

## Порядок игрового тика

[Battle.Step](../../../SpaceBattle.Core/Battle.cs), строки 102–124, выполняет следующие фазы. Это порядок видимости данных, а не только список таймеров.

| Фаза | Реальная работа | Требование для GA |
| --- | --- | --- |
| collect | Списки сущностей, serial sorting и teamCounts | Объявленный snapshot/resource preparation с устойчивым порядком |
| ai | PrepareTactics, squad/target snapshots, затем Decisions | Global preparation отдельно от per-entity kernel, read buffers и lookup |
| thermal-inventory | Heat/Shield, теги, инвентарь и дополнительные own writes | Полный access set, optional Has/TryRead, InlineArray и counter contributions |
| steering | Motion/Transform/Heat, home/target/cover/squad | Snapshot соседей и числовые vector/angle функции |
| weapons | Primary/secondary/turrets, shots sorting, heat/capacity, spawn | Ordered event application с лимитами, shared ownership и ticket allocation |
| carriers | Build requests и Dock repair | Tick/config resources, parent lookup, capacity arbitration |
| physics | ECS → PhysicsService → ECS, затем движение projectile/debris/particle | Различение global numerical service и независимых kernels |
| hits | Contacts → projectiles → debris, последовательный Damage | Упорядоченный fold, не общая сумма повреждений |
| lifecycle | States, tags, docking, destruction, builds, reinforcements | Контроль commit boundaries, реальных ID/serial и ресурсов сервиса |

Есть явные Store.Commit call sites внутри крупной фазы: FlushShots, lifecycle после retired, затем внешний Step и подкрепления. Их backend semantics различаются: SecsStore наследует NativeStore.Commit, который проверяет разрешённость mutation; MorpehStore дополнительно вызывает world.Commit. Следовательно, сами вызовы не являются одинаковыми GPU barriers или deferred commits. GA должен сохранить semantic visibility игры через объявленные группы producers и apply/commit boundaries, а не принудительный commit после каждого Execute. Thermal/Steering/Dock накапливают state requests до lifecycle; Weapon/Secondary/Turrets объединяют shots до FlushShots.

PrepareTactics — whole-world операция с группами и изменением Brain.SquadLeader, а не независимый callback: [TacticalAi.cs](../../../SpaceBattle.Core/TacticalAi.cs), метод PrepareTactics. Во время исследования squad storage обновлён на value buffers с InlineArray[MaxSquadSize], а AiRows заимствуется через CollectionsMarshal.AsSpan внутри фазы без resize. Эти новые плоские представления подходят для explicit resource boundary, но borrowed Span нельзя удерживать через async dispatch. Турели также образуют parent-child связи и имеют собственную тепловую модель: [ArmamentSystems.cs](../../../SpaceBattle.Core/ArmamentSystems.cs), строки 30–185. Для таких задач разрешён общий CPU шаг с объявленной семантикой; GPU placement отдельного этапа не требует менять алгоритм игры.

## Значимые ресурсы вне компонентов

| Ресурс | Где используется | Что требуется определить |
| --- | --- | --- |
| Tick, Config, catalogs и seed | Battle, Presentation, все processors | Immutable tick input, versioned constants, numeric conversion |
| ships/aiRows, tactical/squad indices | Collect/PrepareTactics/TacticalAi | Snapshot lifetime, entity generation, buffer schema и source order |
| teamCounts, occupiedDocks, ownedShips | Carrier, docking, build commit | Детерминированное получение и последовательное распределение лимитов |
| shots/states/builds/retired | CombatSystems, ArmamentSystems | Payload, total order, duplicate/conflict policy, commit boundary |
| serial и entity allocator | SpawnShip, turret/projectile/particle/debris spawning | Семантически значимое назначение идентичности и порядок освобождения |
| reinforcements и WarpWave | CombatSystems, WarpReinforcements | Будущие timers/queues, target/home handles, wave plan и continuation state |
| Counters/Tactics | Gameplay, диагностика и наблюдение | Typed contributions, правила readback; определить влияние каждого счётчика |
| PhysicsService | StepPhysics, sweep, CanSpawn, destruction | Состояние сервиса, access/serialization и числовой профиль |

Context должен разрешать дополнительные own-компоненты и optional tags независимо от ограничения нынешнего ForEach на три ref. Доступы фиксируются в descriptor и проверяются транзитивно; скрытый World/Battle reference не разрешается. Host List/Dictionary могут остаться способом подготовки ресурсов на CPU; portable код получает типизированные представления с фиксированной schema и известными границами. Lazy scoring/target/threat caches относятся к подготовке или явным локальным вычислениям: Ready flags и изменяемые buffers не становятся безопасными shared reads лишь потому, что их результат выводится из immutable snapshot.

Комментарий AiShipSnapshot прямо связывает nongeneric value type с Mono WASM AOT calling conventions. Для generator это конкретный тест specialization и host ABI: author generic API может сохраняться, но generated value/interop bridges нужно квалифицировать по фактическому AOT runtime. Desktop JIT pass не закрывает эту границу.

## Доменный порядок нельзя заменить порядком Entity ID

В игре существуют три уровня идентичности: native ECS handle, публичный EntityKey(Id, Generation) и игровой Serial. ID переиспользуется; Serial растёт при создании разных видов сущностей. [EntityIndexTable](../../../SpaceBattle.Core/EntityIndexTable.cs) специально индексирует snapshot по публичному ID/Generation, а не по Serial. В нескольких алгоритмах равный score выбирает первого по serial-sorted snapshot.

| Алгоритм | Текущий порядок | Проверка при переносе |
| --- | --- | --- |
| Target/home/cover/threat selection | Serial-sorted кандидаты и строгий lower/better comparison | Равные scores и расстояния; меньший ID не обязательно меньший Serial |
| Shots | (Serial, Slot), CombatSystems:40 | Несколько слотов, turret serial, bursts, heat и MaxProjectiles |
| States | (Serial, Kind, Enable), CombatSystems:290 | Enable/disable одной сущности, same-key Other conflict и tie policy |
| Builds | (Team, Order), CombatSystems:346 | Конкуренция за team/home limits, принятые и отклонённые requests |
| Physics contacts | (FirstSerial, SecondSerial), PhysicsService:156 | Begin-contact history и столкновения нескольких тел |
| Swept hit | Минимальная fraction; равная fraction выбирает меньший Serial | Одновременное попадание в две цели |
| Damage | Contacts, затем serial-sorted projectiles, затем debris | Shield/armor/plasma/heat, LastAttacker, смерть и repeated requests |

Для GA добавлен versioned order descriptor: canonical EntityKey остаётся default, но система может объявить стабильный доменный ключ. Event stream может объявлять собственный order key и ordered apply. У GPU/CPU одинаковы ключи и границы объединения. Равные ключи с разным payload нельзя оставлять зависимости List.Sort/append completion; требуется явное правило. Если старый код этого правила не определял, его закрепление отмечается как изменение контракта, а не как уже доказанная совместимость.

Damage в [CombatSystems.cs](../../../SpaceBattle.Core/CombatSystems.cs), строки 209–227, не является коммутативной суммой: поглощение Shield, plasma heat, LastAttacker и AliveShip проверяются после предыдущего попадания. Простое atomic add либо суммирование damages меняет правила. Ordered application может первоначально работать на CPU; GPU только собирает точные requests.

## PhysicsService уже собственный

[SpaceBattle.Core.csproj](../../../SpaceBattle.Core/SpaceBattle.Core.csproj) не содержит Aether PackageReference. Production [PhysicsService](../../../SpaceBattle.Core/PhysicsService.cs) реализует circle dynamics и analytic projectile CCD. Aether остался в [PhysicsStudy](../../../tools/physics/PhysicsStudy.csproj) и [AetherReference](../../../tools/physics/AetherReference.cs). Старый Aether workload и нынешний circle-kernel-v1 нельзя смешивать в baseline или fallback.

Step вычисляет количество substeps из travel, строит контакты в serial order и последовательно решает ограничения, обновляя скорости тел на месте. CorrectPositions затем меняет позиции, может перестроить контакты и повторить solver: PhysicsService, строки 109–246. Наивное параллельное применение пар заменяет последовательную семантику solver и не даёт обещанного результата.

Приоритет — контракт детерминированного CPU сервиса с объявленной state schema, затем отдельное исследование эквивалентного GPU варианта. Нынешний float/double сервис не объявляется безопасным детерминированным fallback только потому, что работает на CPU. Пока числовой контракт и квалификация отсутствуют, он относится к legacy диагностической нагрузке вне GA parity claim.

## Математика шире базового Fixed

[FireControl](../../../SpaceBattle.Core/FireControl.cs) использует стабильный выбор корня quadratic intercept, sqrt, CopySign, normalization и fallback направления. [CircleSweepMath](../../../SpaceBattle.Core/CircleSweepMath.cs) использует mixed double/float, explicit FusedMultiplyAdd, scaled roots, discriminant tolerance, slab tests и круговые углы Minkowski суммы. [Battle](../../../SpaceBattle.Core/Battle.cs) содержит Exp, Atan2, trig и wrap angles. [WarpReinforcements](../../../SpaceBattle.Core/WarpReinforcements.cs) использует ceil/trig и bounded placement loop до 32 попыток.

Это конкретный backlog общей детерминированной математики: vector/dot/cross/length/normalize, sqrt, angle wrapping, sin/cos/atan2, Exp либо определённые коэффициенты damping, quadratic roots и conservative spatial bounds. GPU intrinsics не используются как точный эталон. Требуются range audit, достаточные промежуточные разрядности и отдельные error contracts; Q32.32 +,-,*,/ сами по себе не покрывают эти algorithms.

Разделяются воспроизведение старой float игры и новая deterministic числовая модель. Новый Fixed вариант обязан совпадать между CPU/GPU, но не обязан давать прежний float checksum. Изменение thresholds, tolerance или алгоритма корней требует версии и игрового regression test, а не молчаливой замены формулы. Базовая задача Fixed из другого чата не расширяется без отдельного согласованного объёма.

[RandomStream](../../../SpaceBattle.Core/Presentation.cs), строки 70–76, — xorshift32; seed 0 преобразуется в 1, Unit использует верхние 24 бита и float. Начальные ключи используют seed/serial/tick/slot. Нужно сохранить uint wrap, domain separation и draw/consumption order, определив exact Fixed mapping для Unit/Range.

Particles нельзя признать независимым presentation только по имени. Effect, затем Fragments используют общий serial: [CombatSystems.cs](../../../SpaceBattle.Core/CombatSystems.cs), строки 230–267. Particle creation меняет serial, Entity allocator и seed последующих fragment/spawn операций; [SpawnWarpParticle](../../../SpaceBattle.Core/WarpReinforcements.cs) тоже использует общий allocator. Удаление VFX или смена ParticleMultiplier может менять gameplay state. Для будущего разделения presentation/authoritative нужны dependency analysis и отдельный semantic contract.

## Пространственные запросы имеют разные семантики

[FlatGridLayout](../../../SpaceBattle.Core/FlatGridLayout.cs) уже содержит contiguous snapshot ranges после Seal. Это подходящая граница resource lowering: metadata и member buffers вместо копирования managed объектного графа. Начальный CPU build такого индекса допустим; обязательны generation/lifetime и barriers для GPU потребителя.

[SpatialPointIndex](../../../SpaceBattle.Core/SpatialPointIndex.cs), строки 35–75, возвращает sorted IDs с возможными duplicates. Это широкий набор кандидатов; точное расстояние проверяется потребителем. Outside-grid записи имеют отдельный fallback; nonfinite/infinite query возвращает все записи. [SpatialBoundsIndex](../../../SpaceBattle.Core/SpatialBoundsIndex.cs), строки 47–143, проверяет exact AABB overlap, deduplicates IDs по stamp и возвращает sorted unique result. Большие/outside bounds обслуживаются отдельно; границы округляются наружу через BitIncrement/BitDecrement.

Общий GA query contract не должен смешать candidate vs exact, multiset vs unique set и spatial cache vs semantic order. Будущий Fixed индекс определяет собственное conservative outward rounding в raw units; legacy nextafter нельзя просто заменить произвольным epsilon. Обязательный oracle — brute force для покрытия кандидатов и exact overlaps, включая границы, negative cells, duplicates, stale handles, outside/large bounds, Seal/Add/Clear и capacity. Legacy nonfinite fixtures сохраняются как проверки прежней реализации; Fixed профиль задаёт поведение невозможного для него ввода отдельно.

В [FlatSpatialValidation](../../../SpaceBattle.Headless/FlatSpatialValidation.cs) и [HardwareOptimizationValidation](../../../SpaceBattle.Headless/HardwareOptimizationValidation.cs) уже есть подходящие граничные сценарии. Второй также проверяет внешние ref edits, entity reuse и смену membership. Это новое требование для GA caches: счётчик Set/structural mutations не замечает запись через выданный ref. Базовый portable runner пересобирает актуальные snapshots/upload на BeginTick; частичная инвалидация требует контролируемого write lease или другого проверенного механизма.

## Desktop и Web уже используют одну Core симуляцию

Desktop [Program](../../../SpaceBattle.Game/Program.cs), строки 307–328, накапливает dt и вызывает синхронный Step максимум 32 раза за frame. Renderer получает BattleFrame, но Capture сам читает World; это не разрешает чтение при Pending. Pause, restart и наблюдение камеры должны работать с последним опубликованным снимком и согласованным lifecycle.

В Web [BattleBridge](../../../SpaceBattle.Web/BattleBridge.cs), строки 69–80, экспортирует синхронный Advance(0..600), затем Snapshot. [app.js](../../../SpaceBattle.Web/wwwroot/app.js), строки 1048–1095, ограничивает catch-up бюджет и максимум 12 ticks/frame, обрезая wall-clock backlog. Это не пропуск номеров симуляционных тиков и не основание менять tick input при медленном GPU. Snapshot использует переиспользуемый WASM memory buffer, который может быть заменён при росте: BattleBridge, строки 351–361.

[Production Web](../../../SpaceBattle.Web/SpaceBattle.Web.csproj) — поддерживаемый стабильный net8.0 Mono AOT хост; локальная диагностика Mono interpreter поддерживается отдельно. AOT/browser интеграция уже существует, но GPU/WebGPU pipeline в этом bridge отсутствует; mobile AOT и real device поддержка проверяются отдельно. Для GA нужны nonblocking BeginTick/Advance/Poll и публикация Completed snapshot; сохранённый JS view не должен пережить замену buffer без нового поколения/handle.

Headless остаётся лучшим первым host для parity. Desktop и production Web становятся существующими integration lanes, а не гипотетическими будущими host проектами. Полная миграция frontend и игры производится позже отдельными задачами; её acceptance уже описана в обновлённом плане.

## Текущий checksum не является полным checkpoint

[Battle.Checksum](../../../SpaceBattle.Core/BattleInspection.cs), строки 53–134, хеширует много компонентов, serial, отдельные очереди и настройки, преобразуя EntityKey ссылки в serial либо sentinel. Он не сериализует allocation/generation состояние World и не является восстанавливаемым snapshot. Это полезный same-workload regression check, но не полный GA state schema.

Конкретный пропуск: PhysicsService хранит previousPairs/currentPairs; после Step текущий contact pair set переносится в history следующего тика. BuildContacts сообщает contact только если его не было в предыдущем set: PhysicsService, строки 42, 124, 193–199. ResolveHits применяет gameplay damage от этих begin contacts: CombatSystems, строки 166–173. Поэтому одинаковые ECS поля с разной pair history могут продолжиться по-разному. Constraints пересобираются внутри substep; отдельный persistent warm-start cache здесь не обнаружен и в требования не включается.

Future checkpoint различает хранимую significant state и rebuildable caches, включает ID/generation/free-list, timers/queues, order/RNG и service history. PhysicsService имеет независимые body slots/freeIds, а ECS PhysicsLink.Body ссылается на этот индекс: PhysicsService, методы AddShip/RemoveShip. Требуется сохранить mapping либо восстановить сервис из canonical ships с remap всех PhysicsLink и зависимых resources; потерянная или изменённая связь не считается корректным restore. Каждый объявленный rebuildable cache проверяется replay continuation и oracle; потеря ContactPairsHistory не должна пройти сравнение. Render frames и прежние benchmark goldens не заменяют этой проверки.

## Как изменены приоритеты

1. Сначала зафиксировать SystemContext, дополнительные component accesses, resource schemas и commit/order descriptors; простой Movement остаётся smoke, не достаточной игровой приёмкой.
2. Ввести шесть обязательных gameplay fixtures: Movement, TargetRead, ThermalInventory, ShotBurstCapacity, MultiHitDamage и CarrierCapacity. Добавить spatial/parent/docking и host fixtures для соответствующих возможностей.
3. Спланировать отдельный AdvancedGameMath профиль поверх общего Fixed; не блокировать integer compiler prototype ожиданием всей игровой математики.
4. Определить exact CPU service admission и snapshot/continuation для собственного physics; перенос solver на GPU не является обязательным условием acceleration остальных систем.
5. Квалифицировать full game отдельно: все significant CPU/GPU стадии должны соблюдать один контракт; legacy float service нельзя спрятать за fallback и считать полную игру доказанной.

## Источники и воспроизведение исследования

Авторитетны текущие исходники, включая несохранённые в commit изменения. Старые документы и замеры могут относиться к Aether workload; состояние проверено по Core project и PhysicsService. В этом исследовании source SHA256 сравниваются до и после подготовки документов; изменение затронутого файла требует повторного чтения перед фиксацией вывода.

Для последующей проверки читать Battle.Step и processors, затем PhysicsService/queries, NativeStore/SecsStore, host loops и соответствующие Headless validation fixtures. Проверка runtime и timings выполняется отдельным conformance/benchmark шагом с зафиксированным build и без одновременных измерений другой нагрузки. Игровые source файлы и текущие результаты этим исследованием не изменяются.
