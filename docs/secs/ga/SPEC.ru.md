# Техническое задание GA SECS

Дата: 12 октября 2026 года. Версия требований: 0.2, уточнена по текущей SpaceGame симуляции. Статус: проект для реализации. Имена новых API и модулей ниже являются предложениями; они не описывают уже существующие возможности SECS.

> Целевое требование: SECS должен позволять писать одну симуляцию и выполнять её на CPU или поддерживаемом GPU с побитово одинаковым результатом. Исполнитель адаптируется к возможностям устройства без изменений игровой логики.

GA означает GraphicsAccelerated. Зафиксированный принцип распространяется на один поддерживаемый контракт систем и чисел. Производительность, размещение данных и число физических проходов могут различаться; входы, порядок значимых операций, завершённое состояние и события должны совпадать.

Документы пакета: [план реализации](IMPLEMENTATION_PLAN.ru.md), [правила написания систем](GUIDELINES.ru.md), [исследование SpaceGame](SPACEGAME_REVIEW.ru.md). Основа интеграции: [развитие SECS](../DEVELOPMENT_PLAN.ru.md), [задача Fixed](../prompts/03_FIXED.ru.md), [план проверки детерминизма](../../SECS_DETERMINISM_PLAN.ru.md), [границы изменений потребителей](../RELATED_CHANGES.ru.md).

Навигация: [контракт детерминизма](#общий-контракт-детерминизма) · [числа](#числа-и-интеграция-с-fixed) · [системы](#поднабор-c-и-api-систем) · [runner](#runner-и-жизненный-цикл) · [fallback](#возможности-устройства-и-cpu-fallback) · [приёмка](#приёмка-и-производительность).

## Цель и область реализации

Результат разработки — opt-in возможность существующего SECS. Компоненты, системы, запросы, стадии и порядок симуляции описываются один раз. CPU и GPU получают разные реализации одного плана. Разработчик игры не пишет HLSL, GLSL, MSL или отдельный GPU алгоритм, не выбирает bindings и не управляет workgroup внутри игровой системы.

GA должна работать без окна и renderer, в том числе на сервере. Графический API renderer и compute backend выбираются независимо. Совместное использование графических ресурсов является отдельной оптимизацией с проверяемыми барьерами; базовый путь не зависит от него.

Поддержка ПК и мобилок достигается матрицей backend/API/device capabilities и CPU fallback. Доступность GPU compute на каждом устройстве не обещается. Целевые платформы: Windows, Linux, macOS, Android и iOS. Целевые API: Vulkan, D3D12, Metal, D3D11, OpenGL compute, OpenGL ES compute и WebGPU. API или версии без достаточного compute получают CPU маршрут. Поддержка GPU публикуется только для фактически квалифицированных сочетаний.

В эту задачу не входят переписывание игровой архитектуры, массовая миграция SpaceBattle, перенос физического сервиса игры, обновление старых результатов бенчмарков и разработка сетевого транспорта. GA предоставляет основу для replay, prediction и rollback; реализация сетевого протокола или хранения checkpoints не возникает автоматически. Устройство реальной игры определяет обязательные framework contracts и fixtures, даже когда полная миграция её исходников выполняется отдельным этапом.

## Текущее состояние и зависимости

В [SECS](../../../SECS/SECS.csproj) используется .NET 8. [Archetype](../../../SECS/Archetype.cs) хранит колонки компонентов в host memory. Поля внутри отдельной структуры сохраняют её layout; все X внутри Position не образуют отдельную непрерывную колонку. [Query](../../../SECS/Query.cs) последовательно вызывает [IQueryProcessor](../../../SECS/IQueryProcessor.cs) с изменяемыми ref. [World](../../../SECS/World.cs) регистрирует Action<World>; структурные изменения выполняются сразу, удаление переставляет последнюю строку чанка.

В текущей библиотеке отсутствуют GA compiler, device buffers, граф вычислительных зависимостей и GPU executor. Fixed, DeterministicRunner, команды и параллельное исполнение описаны отдельным планом. Они являются зависимостями GA, а не подтверждённой реализацией.

Существующие ForEach, Chunks, AddSystem, Run и RunAll сохраняют последовательную legacy семантику. Их нельзя скрыто распараллелить или перенаправить на GPU. Portable API подключается явно и имеет одинаковое поведение на CPU и GPU. Произвольный старый Action<World> не становится portable системой после включения флага GA.

SpaceGame реализован проектами SpaceBattle.*. Текущий Core использует собственный circle PhysicsService, а Aether находится только в tools/physics для сравнения. Battle.Step задаёт фазовую последовательность; processors обращаются к дополнительным own-компонентам, соседям, Config/Tick, queues и counters через managed Battle. Поэтому одного bridge текущего IEcsStore к device memory недостаточно. В production уже существует net8.0 Mono AOT Web host; WebGPU и mobile runtime этим не подтверждены. Подробные основания и source pointers приведены в [исследовании](SPACEGAME_REVIEW.ru.md).

## Обязательные требования

| ID | Требование | Проверяемый результат |
| --- | --- | --- |
| GA R01 | Один исходник системы для исполнителей | Одна декларация Execute порождает CPU и GPU пути без ручных shaders и ветвления игровой логики по backend |
| GA R02 | Побитовый детерминизм | Совпадают канонические значения всех полей и события после каждой завершённой системы и тика |
| GA R03 | Общий контракт работы | Read/Write, snapshots, commands, fault и порядок задач совпадают у исполнителей |
| GA R04 | Roslyn диагностика | При GA сборке IDE и build проверяют весь достижимый код системы и объясняют ошибку или CPU fallback |
| GA R05 | Проверка на старте | До первого тика выбран совместимый план с причиной каждого CPU этапа |
| GA R06 | Работа без GPU | Auto исполняет те же системы на CPU без изменений компонентов или логики |
| GA R07 | Разные API и устройства | Platform adapters используют capabilities и metadata своего артефакта |
| GA R08 | Наблюдаемое исполнение | Отчёт показывает фактические backend, API, device, profile и fallback; CPU результат не выдаётся за GPU тест |
| GA R09 | Версионированные правила | Поднабор, IR, числовой контракт, schema и артефакты согласованы по версиям |
| GA R10 | Совместимость SECS | Legacy API и существующие потребители сохраняют поведение без включения GA |
| GA R11 | Автономное развёртывание | Выполнение не требует установленного на устройстве Slang или доступа к сети |
| GA R12 | Полная стоимость ускорения | Измеряется весь тик с packing, transfers, barriers, events и commit, а не только kernel |
| GA R13 | Полный контекст системы | Дополнительные own/optional accesses, tick resources, snapshot buffers и lookup описаны и проверены без Battle/World closure |
| GA R14 | Доменный порядок | Source/event order descriptors сохраняют serial/slot/team и последовательный apply, независимо от Entity ID reuse и backend |
| GA R15 | Контракт запросов | Candidate/exact, duplicates/unique, boundaries и invalidation spatial ресурсов определены одинаково для исполнителей |
| GA R16 | Общие CPU стадии | Snapshot preparation, ordered apply и numerical services входят в один plan с явными доступами и admission contract |
| GA R17 | Полнота продолжения | Canonical state включает allocator/generation, значимые resources и service history; restore/continue проверяется отдельно от checksum |
| GA R18 | Существующие host и внешние edits | Headless/desktop/Web читают только Completed; BeginTick snapshots учитывают допустимые ref edits и membership changes |

## Общий контракт детерминизма

Гарантия задаётся как одинаковая последовательность Completed состояний при одинаковых начальном состоянии, привязанных к тикам командах и SimulationContractId. Последний включает числовую версию, schema компонентов, семантику систем, порядок стадий и систем, алгоритм RNG и правила задач/событий. Смена программы или контракта требует явно совместимого replay формата либо нового идентификатора.

Каноническое состояние включает Entity ID и поколения, все объявленные поля компонентов, ресурсы систем, очереди отложенных действий, будущие timers и RNG состояние, если они влияют на продолжение. Сериализация перечисляет определённые поля в заданном порядке и endian. Padding, указатели, native handles, время исполнения и размещение в device memory в сравнение не входят. Побитовое равенство означает одинаковые канонические значения, а не равенство двух разных физических buffer layouts.

Проверка хешей ускоряет поиск расхождений; окончательная проверка маленьких fixtures сравнивает полные снимки и последовательность событий. Checkpoints после системы доступны тестовому instrumentation и не публикуют изменяемый World наружу до Completed тика. При различии отчёт указывает тик, систему, сущность, компонент, поле, expected/actual raw и source location. Продолжение из совпавшего снимка дополнительно проверяется следующими тиками.

Системы выполняются по PreUpdate, Update, PostUpdate и фиксированному порядку регистрации. Первый GA executor параллелит только работу внутри одной системы. Межсистемный параллелизм и fusion разрешаются позже, после доказательства эквивалентности.

Default канонический список совпавших сущностей упорядочен по (Entity.Id, Entity.Version). Система может объявить стабильный доменный order descriptor, например Identity.Serial с полным tie-break; этот descriptor входит в SimulationContractId и действует одинаково на CPU/GPU. Нельзя считать переиспользуемый Entity ID эквивалентом игрового Serial. Базовая логическая задача содержит до 64 элементов уже упорядоченного списка, как в существующем плане runner. ChunkCapacity, число CPU workers, SIMD ширина, GPU group size и порядок завершения не меняют job ID и порядок результатов. Параметр логического разбиения версионируется. Это opt-in расширение default порядка общего runner, требующее отдельного интеграционного diff.

Portable processor не содержит скрытого изменяемого состояния, зависящего от физического потока или группы. Явные локальные значения относятся к вызову сущности. Ресурсы и накопление результатов описываются контрактом; legacy processor с накоплением в полях остаётся вне автоматического GPU переноса.

## Числа и интеграция с Fixed

Основной числовой контракт берётся из задачи [Fixed](../prompts/03_FIXED.ru.md): signed Q32.32, long Raw, 8 байт, nearest ties to even для умножения/деления и FromRatio, checked overflow независимо от C# checked-контекста, ошибки деления на ноль. Подмодуль GA не создаёт другой Fixed и не меняет округление под устройство.

GPU lowering обязан точно воспроизводить расширенные промежуточные вычисления. Native int64/int128 не являются обязательными возможностями минимального GPU: предусмотреть реализацию Raw парой uint32 и широких операций последовательностями 32-битных операций, включая carry, знаки, division, halfway и проверку диапазона. Состав операций допускается ограничить только с явной диагностикой и CPU fallback соответствующей системы. Подмена Q32.32 на Q16.16, float или меньшую точность запрещена.

Оптимизированные native варианты вводятся после сравнения с общей реализацией и независимым BigInteger oracle. Golden vectors числового проекта используются повторно. Для int/uint сохраняется явно установленная C# семантика: unchecked arithmetic/casts работают modulo 2^32, checked дают overflow fault, division округляет к нулю, remainder имеет знак делимого. SECS однозначно фиксирует fault для деления на ноль и signed MinValue / -1 либо MinValue % -1 в обоих contexts; допустимая спецификацией C# вариативность unchecked случая не переносится между backend. Маска shift count равна 31 для 32 бит и 63 для 64 бит, signed right shift является арифметическим. Поведение long/ulong воспроизводится с соответствующей разрядностью. Любые случаи undefined behavior целевого языка реализуются через определённые операции и проверки. Overflow-policy является частью каждой IR операции, а не свойством случайных compiler flags. [Целочисленные division/remainder в спецификации C#](https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/expressions#12123-division-operator).

GPU не обязан бросать CLR exception из kernel. Lowering возвращает FaultRecord. Ключ первого fault — (tick, stage, systemOrder, kernelOrder, sourceOrderOrdinal, evaluationOrdinal). SystemOrder относится к logical system/CommitGroup, kernelOrder равен нулю для одиночной системы. SourceOrderOrdinal назначается по объявленному canonical/domain source descriptor до dispatch с учётом sourceKind; без сущности используется упорядоченный logicalJobId. Entity ID/generation и domain key сохраняются в payload для диагностики. EvaluationOrdinal увеличивается при фактической оценке потенциально ошибочной операции внутри вызова, включая повторения loop; порядок аргументов и subexpressions соответствует C# и фиксируется в IR. Каждый вызов прекращается после своего первого fault, затем выбирается минимальный ключ среди вызовов. Source ID указывает место кода, но не заменяет ordinal. Ключ должен быть уникален; дубликат разного payload является нарушением executor, а не произвольным tie-break. CPU boundary выдаёт тот же публичный тип числовой ошибки с общей информацией runner. Частично изменённое состояние не публикуется. Try/catch внутри portable системы v1 запрещён.

Математические sqrt/trig/vector kernels входят в профиль только после собственного числового контракта и тестов. Float/MathF и преобразование Fixed в float для значимой симуляции не допускаются в исходном строгом профиле. Преобразование для renderer выполняется за границей kernel. Дополнительная математика согласуется с владельцем общего числового модуля; изменение уже выбранного контракта требует версии и анализа совместимости.

По SpaceGame выделяется отдельный AdvancedGameMath backlog: dot/cross/length/normalize, sqrt, sin/cos/atan2/wrap angles, damping Exp либо определённые заранее coefficients, quadratic intercept roots и conservative bounds. Range audit проверяет полиномы, discriminant, промежуточные квадраты, zero direction, tangency и tolerance в raw units. Wide operations не обязаны помещаться в публичный Fixed после каждого шага формулы. Существующие float/double/FMA algorithms не считаются автоматически перенесёнными; новая numerical model получает собственную версию и game regression fixtures. Базовая задача Fixed в другом чате сохраняет свой объём.

## Поднабор C# и API систем

Нормативная таблица и diagnostics находятся в [GUIDELINES](GUIDELINES.ru.md). Требуется проверка достижимого call graph, а не только текста Execute: helpers, свойства, operators, generic specialization и константы проверяются транзитивно. Код из сборки без исходников разрешён только через известную intrinsic реализацию или явно проверенный контракт библиотеки. Присутствие unmanaged у типа само по себе не доказывает переносимость.

Предлагаемая форма системы:

```csharp
// Предлагаемый API. Position/Velocity ниже содержат Fixed поля.
[SecsSystem]
public partial struct Movement
{
    public void Execute(Entity entity, ref Position position, in Velocity velocity)
    {
        position.X += velocity.PerTickX;
        position.Y += velocity.PerTickY;
    }
}
```

Регистрация системы добавляет её в общий runner. Ref/in задают базовое право доступа; compiler проверяет фактическое поведение и aliases. Generator создаёт адаптер к SECS CPU, schema, доступы, запись в plan и таблицу GPU артефактов. Source generator добавляет код, не переписывая произвольные методы пользователя. Если CPU lowering генерирует отдельное тело, оно выводится из того же IR; исходное Execute используется для отладки лишь при доказанной эквивалентности.

### Контекст и ресурсы игровой системы

Для gameplay систем требуется scoped SystemContext, который компилятор понижает в известные handles/buffers. Его interface задаёт Resource<T> для замороженных tick parameters, Own<T> для объявленного собственного компонента, Has/TryRead для optional membership и поколений, ReadSnapshot<T> для соседей, Emit/Commands для результатов. Имена методов предварительные. Пример сигнатуры: Execute(in SystemContext context, Entity entity, ref Inventory inventory, ref Hull hull). Дополнительные own Shield/Heat объявляются в access descriptor; ctx.Own не разрешает ref на другую сущность.

Типизированная форма контекста устраняет нынешний предел трёх query refs без универсального mutable World в kernel. Query membership и дополнительные accesses задаются отдельно: отсутствие требуемого own-компонента даёт определённый fault либо проверенную optional ветвь, а не скрытое изменение состава query. Resource schema фиксирует ownership/lifetime, Read/Write, число элементов, packing, aliasing, значимость для continuation и policy limits.

Snapshot buffers, например AiRows/parent mounts/cell ranges, могут подготавливаться на CPU и читаться GPU через generated ReadBuffer/lookup intrinsics. Managed List/Dictionary остаются host preparation implementation, а не разрешёнными объектами в portable code. Borrowed Span из resizeable host list не удерживается через dispatch. Lazy caches с Ready flags подготавливаются отдельной стадией либо становятся локальными, без скрытых гонок. InlineArray с фиксированным размером, nested value structs, enum switch и bound-checked indexed access входят в конкретный backlog v1; InventoryItems[16] и SquadMembers[4] являются обязательными ABI fixtures. Generated generic value/interop bridges отдельно проверяются на Mono AOT, включая при необходимости nongeneric specialization.

Counters/Tactics выдаются как typed contributions и объединяются по заданному правилу. Shared counter += не разрешается без такого контракта. Tick/resources и источники RNG входят в replay. Нельзя исключать VFX только по имени: если spawn particles использует общий serial/allocator или влияет на RNG/лимиты, соответствующие события относятся к значимой симуляции. Разделение presentation state допускается после анализа зависимостей и отдельной версии правил.

Включение GA задаётся build property, предлагаемый вариант SECSGraphicsAccelerated=true. Он включает GPU подготовку и GA analyzer rules независимо от наличия GPU у машины разработчика или пользователя. Runtime Auto на CPU продолжает использовать проверенный контракт. Когда GA отключена, GPU специфические предупреждения выключены; общие правила явно подключённого детерминированного API сохраняются. Фактическое железо не может включать или выключать Roslyn анализ задним числом.

Анализатор различает три исхода: portable CPU/GPU код; безопасное детерминированное CPU исполнение при отсутствии GPU lowering; нарушение общего контракта. Третий исход является ошибкой и не исправляется fallback. Пример: чтение часов, неопределённый порядок общей Random и гонки не разрешаются только потому, что система работает на CPU.

Безопасный fallback разрешается только для операции с определённой CPU семантикой, подтверждёнными доступами и числовыми правилами. Если компилятор не может проверить код или библиотеку, требуется ошибка либо отдельная явно оформленная host стадия за границей переносимой симуляции. Скрытое исполнение неизвестного callback под видом детерминированной системы запрещено.

## Доступ к миру и структурные изменения

Система объявляет Read, Write и ReadWrite. Запись разрешена в свой элемент. Чтение компонентов других сущностей выполняется через проверенный lookup с Entity ID и поколением. Если читаемый тип одновременно записывается системой, источником служит неизменяемый snapshot начала системы. Записи других сущностей выполняются через events/reduction/commands, а не через произвольные shared refs.

Начальный GPU профиль может отклонять read snapshot соседей и использовать безопасный CPU маршрут до реализации lookup. После добавления такой возможности snapshot обязателен у обоих исполнителей. Нельзя сохранять CPU семантику in-place чтения соседей и объявлять её равной GPU snapshot.

Default Commands/events получают полный ключ общего runner (tick, stage, systemOrder, sourceKind, sourceId, sourceGeneration, localOrdinal): sourceKind=0 для сущности с ID/generation, sourceKind=1 для logicalJobId с sourceGeneration=0. LocalOrdinal — общий возрастающий счётчик commands/events одного источника; источники разных видов не сталкиваются. Для самостоятельной системы этот default сохраняется без изменения.

Grouped stream может объявить versioned order descriptor: префикс (tick, stage, commitGroupOrder), затем domain key, например shot (Serial, Slot), state (Serial, Kind, Enable) или build (Team, Order), и полный tie-break (producerOrder, sourceKind, sourceId, sourceGeneration, localOrdinal). ProducerOrder отличает kernels одной группы; systemOrder исходного producer не должен раньше domain key разрушить объединение запросов из Weapon/Secondary/Turrets. Default групповой stream сохраняет producer order перед source subkey. Source descriptor, ключ, tie policy и stream/commit boundary входят в contract. Сбор в конкурентный append buffer не назначает порядок и Entity ID. Реализация count/prefix/write либо последующая канонизация сохраняет объявленные ключи.

EventApply имеет два разных режима: exact reduction с доказанной эквивалентностью или ordered fold по событиям. MultiHitDamage из игры требует второго: Shield, plasma heat, LastAttacker, AliveShip и death зависят от предыдущих событий. Generic сумма damage не заменяет этот алгоритм. Состояния с одинаковым legacy sort key и разным payload требуют явной tie policy; закрепление ранее не определённого порядка версионируется. Fault selection использует ordinal в объявленном source order и evaluationOrdinal; backend не выбирает fault по физическому placement.

Create/Destroy/Add/Remove откладываются до объявленного semantic commit barrier: всей standalone системы либо CommitGroup, а не обязательно каждого producer kernel. Создание выдаёт ticket ключа Create; реальные ID назначаются при каноническом commit. В первом выпуске обращаться к ticket могут только следующие commands того же источника с большим localOrdinal; обмен между источниками до commit запрещён. Domain ordering обязан сохранять Create-before-use, иначе descriptor отклоняется. Порядок free list и поколения фиксируется в общем CPU runner. Duplicate destroy, stale generation, отсутствие компонента, несколько add/remove и обращение к ticket проверяются одинаковыми контрактными тестами. Structural commit первоначально может выполняться на CPU без нарушения GA: это общая инфраструктура, не отдельный алгоритм игры.

Бюджеты событий и памяти имеют явные пределы и одинаковую реакцию исполнителей. Нельзя терять события на слабом GPU. При недостатке памяти используются точные дополнительные проходы/партии либо решение о CPU до тика. Переполнение фиксированного framework command buffer является общей ошибкой контракта. Игровой лимит MaxProjectiles/team/home/repair slots может по правилу игры отклонять request; accepted/rejected результат и порядок распределения лимита входят в события и replay. Такой семантический отказ отличается от технической потери события.

### Пространственные queries и snapshots

GA query resource определяет candidate superset или exact predicate, duplicates/unique semantics, sorted output/tie key, validity bounds и conservative rounding. Point candidates и exact AABB unique result нельзя объединять в одну неопределённую операцию. CPU построение flat metadata/member buffers с GPU потребителем является допустимым shared планом. Outside-grid/large bounds и исчерпание capacity не должны давать false negatives. Доказательство — independent brute-force oracle и boundary/duplicates/stale-generation fixtures.

Переиспользование snapshot/lookup/spatial caches между тиками требует корректной инвалидации. Legacy external ref writes обходят счётчик Set: исходный безопасный путь обновляет snapshots/uploads на BeginTick и барьере соответствующей стадии. Dirty ranges/version counters добавляются только с write lease/instrumentation либо доказанным механизмом фиксации всех допустимых writes. Нельзя сохранить старый cache из-за отсутствия structural mutations.

## Runner и жизненный цикл

Используется один DeterministicRunner из [общего плана](../DEVELOPMENT_PLAN.ru.md). GA добавляет executor и device lifecycle; второй scheduler, второй счётчик тиков и отдельная модель команд не создаются. BeginTick фиксирует входы. Advance с бюджетом логических задач возвращает Pending или Completed и учитывает незавершённый GPU dispatch, а не только факт его отправки.

Общий plan содержит EntityKernel, SnapshotPreparation, OrderedApply/Commit и зарегистрированный CpuService. Logical system может быть одиночным kernel с default commit либо явным CommitGroup нескольких producer kernels, которым принадлежит общий stream и завершение OrderedApply/Commit. Между kernels устанавливаются data barriers и snapshot epochs с указанной видимостью; структурные requests остаются отложенными до границы группы. Так Thermal/Steering/Dock могут собирать state requests до lifecycle, а Weapon/Secondary/Turrets — shots до FlushShots. Group semantics являются opt-in расширением общего runner, не скрытой сменой поведения legacy систем. Service объявляет входы/выходы, numerical contract, state schema, fault semantics и continuation. Exact CpuService может постоянно оставаться на CPU, сохраняя acceleration остальных систем. Legacy float PhysicsService или неизвестный helper без этих доказательств не допускается в общий parity claim через fallback.

Во время Pending runner эксклюзивно владеет World. Внешние ref, accessors и chunk pointers не используются; renderer читает последний Completed снимок. CPU/GPU обмен выполняется на согласованных границах. После завершения устройства применяются барьеры, fault check, канонический merge и commit, затем публикуется результат.

Headless, desktop Update/Render и production Web Advance/Snapshot являются отдельными host conformance lanes. Для async GA они пользуются неблокирующим Advance/Poll и не вызывают Capture, checksum или observer, читающий живой World, во время Pending. Pause/restart/dispose соблюдают drain; wall-clock catch-up не пропускает logical ticks. Web frame ABI и JS view lifetime отделены от device buffer layout; смена memory buffer публикует новое поколение/handle. Уже существующий Mono AOT путь не заменяет mobile device или WebGPU qualification.

Переключение backend на старте или перед новым тиком безопасно при переносе полного Completed состояния. После частично выполненного dispatch обычный CPU fallback запрещён: невозможно обещать отсутствие повторных событий и частичных записей. Ошибка устройства/системы переводит runner в Faulted, затем выполняется drain и освобождение ресурсов по общему контракту.

Первый выпуск не обещает транзакционный rollback. В Faulted World остаётся закрытым, дальнейшее действие — DiscardWorld либо восстановление из проверенного checkpoint, если оно отдельно реализовано. Только такое восстановление позволяет повторить тик на CPU. Нельзя молча выдавать Completed после device loss. Анализаторный fallback для системы и device loss являются разными ситуациями.

Canonical checkpoint описывает allocator/generations/free list и significant service resources, а не только component checksum. Для circle PhysicsService нужен last-tick contact pair history: он определяет begin-contact и gameplay damage следующего тика. Независимая body-slot/free-ID модель и связь PhysicsLink.Body сохраняются либо явно реконструируются с remap всех зависимых links. Исключение physical handle из canonical fields допустимо только через объявленную logical-handle schema и доказательство, что raw номер не используется как игровое число. Rebuildable cache явно помечается и восстанавливается из significant state; correctness проверяется продолжением после restore. Текущий Battle.Checksum не выполняет этот контракт и остаётся regression diagnostic прежней нагрузки.

## Compiler и IR

Последовательность сборки: Roslyn semantic model/IOperation → проверка call graph и правил → типизированный SECS IR → CPU lowering и Slang source → целевые artifacts → package manifest. Shader строки не получаются регулярными заменами текста C#.

IR описывает типы/ширины, control flow, точные arithmetic ops, conversions, reads/writes, bounds и entity generation checks, snapshots, events, commands, logical source keys и fault propagation. Инварианты проверяются до lowering и после оптимизации. Версионируются IR schema, subset profile и intrinsic registry. Generic типы/методы специализируются на известных типах; динамическая CLR диспетчеризация в v1 отсутствует.

Каждая оптимизация обязана сохранять контракт. Reassociation, native math approximation, смена rounding, зависимость RNG от lane и изменение порядка reductions запрещены без отдельного доказательства. Source map связывает C# → IR → generated Slang → target diagnostics. Debug path показывает исходный вызов и canonical state diff.

## Slang и контракт графического runtime

Предпочтительный GPU compiler — Slang. Собственный IR остаётся владельцем игровой семантики, Slang используется для генерации целевых форматов. Преимущество перед альтернативами проверяется на выбранном поднаборе; SPIRV-Cross может быть вспомогательным маршрутом для отдельного API после независимой приёмки, а не обязательной общей основой.

Фиксируются точная Slang version, compiler/downstream toolchain, profile и options. Каждая платформа получает свой compilation target и reflection. Структура параметров и layout зависят от target; при target-specific defines нужны отдельные sessions. Эти возможности описаны в [Slang Compilation API](https://shader-slang.org/slang/user-guide/compiling). Конкретная конфигурация SECS выбирается на этапе GA 01.

Build adapter предпочтительно использует Compilation API и getLayout(targetIndex). Допустим CLI с reflection JSON, если зафиксированная версия выдаёт всю требуемую информацию. Для implicit scope/container bindings JSON обязан содержать version не ниже 1.1 и полные globalScope/per-entry scope; более старый или неполный JSON отвергается либо заменяется API route. Текущая документация описывает API и -reflection-json; route проверяется на реальном compiler. [Slang Reflection](https://shader-slang.org/slang/user-guide/reflection.html).

Для каждого entry point bundle содержит:

| Группа | Обязательные данные |
| --- | --- |
| Идентичность | SystemId, source/IR digest, SimulationContractId, component schema и numeric/subset/IR versions |
| Toolchain | Slang/downstream version, target format/profile, options, defines, specialization и entry point |
| Resource mapping | LogicalResourceId → descriptor set/space, binding/register/index, resource kind, access и count; отдельные Metal namespaces |
| Data layout | Размер, alignment, member offset, array/matrix/element stride, layout uniform/storage/push parameters, packing adapter |
| Device requirements | Compute profile, используемые инструкции, limits, workgroup variants, shared memory и storage requirements |
| Integrity | Artifact и reflection digests, manifest schema, полная связь metadata с тем artifact, который загружается |
| Debug | Source map и диагностические идентификаторы |

Runtime получает logical resource map из плана SECS, а физические indices — из metadata конкретного target. Нельзя считать первый buffer равным 0, одинаковыми index spaces или переносить offsets между API. Автоматически добавленные global parameter containers и удалённые неиспользуемые resources учитываются. Явные Slang binding/layout options могут упростить соответствие, но не заменяют reflection и integration tests.

Manifest хранит полный declared layout и отдельно per-entry-point usage, полученный через getEntryPointMetadata/isParameterLocationUsed либо доказанно эквивалентный маршрут. Base layout reflection не считается сведениями об удалении ресурсов; отсутствие usage metadata запрещает угадывать фактически используемые bindings. CLI-only route допускается только с достаточными данными для выбранной политики runtime. [Различие layout и usage в Slang](https://shader-slang.org/slang/user-guide/reflection.html#determining-whether-parameters-are-used).

D3D artifact pipeline явно включает подходящий downstream HLSL compiler: DXC для соответствующего D3D12 профиля, FXC или другой проверенный DXBC маршрут для D3D11. Bundle обозначает настоящий формат загружаемого artifact; HLSL source нельзя маркировать как DXIL/DXBC. Все downstream версии и options входят в identity пакета.

Compile-time manifest нормализуется в собственную стабильную schema SECS. Если layout различается, generator создаёт точный pack/unpack; значение Fixed и поля остаются теми же. CPU ABI, canonical serialization и device layout разделены. Runtime не использует CLR sizeof/Marshal layout как единственный источник shader layout.

Каждый adapter отвечает за pipeline/resource creation, descriptor/register mapping, alignment, memory visibility, barriers/fences, submit/drain, readback и device loss. Headless путь тестируется отдельно. Кэш pipeline привязан к artifact/options/schema/API/device compatibility; при несовпадении metadata он отвергается. Native compiler libraries нужны build tools, а не обязательному mobile runtime. Финальная native pipeline compilation самим API выполняется там, где её требует платформа.

## Возможности устройства и CPU fallback

На старте runtime перечисляет доступные adapters и проверяет API version, device features/limits, наличие совместимого bundle, создание ресурсов и pipeline. Проверка capability битов дополняется коротким conformance probe нужного числового и binding профиля. Probe не заменяет квалификацию платформы. Выбор устройства не должен зависеть от renderer window или скрытого предпочтения дискретной карты.

Предлагаемые runtime политики:

| Политика | Поведение |
| --- | --- |
| CpuOnly | Тот же GA план и source, только CPU; используется для эталона и устройства без GPU |
| Auto | Минимально достаточный точный GPU профиль, иначе детерминированный CPU fallback; расширенный профиль только при совместимости |
| RequireGpu | Все заявленные GPU compute системы обязаны реально исполняться на GPU; невозможность даёт StartupFailure до первого тика |

RequireGpu допускает host orchestration, transfers и общий structural commit; он запрещает скрытый CPU маршрут самих проверяемых kernels. Выбор Auto может дать смешанный план с явно указанными CPU системами. Его зависимые GPU стадии синхронизируются на границах систем. Переключение не меняет input, RNG, числовой профиль или число логических тиков.

Минимальный compute профиль Portable32 требует storage read/write и точную реализацию выбранных 32-битных операций. Native int64, wide atomics, subgroup/wave intrinsics, bindless и FP64 не являются обязательными. Требования к конкретным buffers, bindings, dispatch и memory вычисляются из bundle. WideFixedEmulated использует 32-битные части; NativeWide — проверенную оптимизацию с тем же числовым контрактом. Их названия и точные limits фиксируются в capability registry, а не в коде игры.

Startup report содержит requested/selected policy, backend/API/device/driver, contract и artifact IDs, profile, system-by-system execution и reason codes. Причины включают NoComputeApi, MissingCapability, MissingArtifact, IncompatibleManifest, PipelineCreationFailed, ProbeFailed, ResourceBudgetExceeded, CompilerCpuOnly и UnqualifiedConfiguration. Fallback предупреждение не теряется в логах; программа может показать его через обычную diagnostics API.

Для неиспытанного сочетания default — CPU либо явно включённый experimental профиль с отражением статуса. Experimental не считается релизной гарантией. Потребитель получает минимальный корректный набор исполнения даже при отсутствии GPU compute.

## Платформы и квалификация

На дату ТЗ таблица Slang отмечает Metal/WebGPU как experimental, а Vulkan/D3D как поддерживаемые направления. Компилятор на desktop и compiler target для мобильного shader не являются готовым mobile runtime. [Текущая матрица Slang](https://github.com/shader-slang/slang#limitations-and-support). OpenGL не указан официально поддерживаемым target в [target compatibility](https://github.com/shader-slang/slang/blob/master/docs/target-compatibility.md); маршрут GL/GLES требует отдельного исследования.

| Направление | Обязательная проверка GA | Роль в плане |
| --- | --- | --- |
| Windows x64/ARM64 | CPU; D3D12, затем Vulkan/D3D11 для заявленных профилей | Одна из трёх обязательных desktop ОС |
| Linux x64/ARM64 | CPU и Vulkan, headless | Одна из трёх обязательных desktop ОС |
| macOS ARM64, x64 при заявлении | CPU и Metal на реальном GPU | Одна из трёх обязательных desktop ОС; Slang route квалифицировать |
| Android ARM64 | AOT host, CPU, Vulkan; GLES 3.1+ compute отдельным маршрутом | Реальные Adreno и Mali минимум для заявления этих семейств |
| iOS ARM64 | AOT host, CPU и Metal, упаковка artifacts | Реальный Apple device; не заменяется simulator/macOS |
| OpenGL compute | Версия 4.3+ и фактические limits на заявленных ОС | Отдельный backend/toolchain и проверка |
| WebGPU | CPU host и WGSL/runtime route, ограничения среды | Дополнительный переносимый API; browser host является отдельной интеграцией |
| API без достаточного compute | CPU fallback до первого тика | Поддержка устройства без утверждения GPU acceleration |

Версии GL/GLES здесь обозначают начало compute возможностей, а не гарантию нужного SECS профиля. [OpenGL compute](https://wikis.khronos.org/opengl/Compute_Shader), [OpenGL ES 3.1](https://registry.khronos.org/OpenGL/specs/es/3.1/es_spec_3.1.pdf).

Каждое заявление о поддержке содержит OS/architecture, GPU family, API/profile, compiler/runtime/driver и digest набора тестов. Минимум трёх desktop ОС требуется для desktop release; это не замена проверки NVIDIA/AMD/Intel/Apple. Mobile release отдельно требует Android и iOS. Один hosted GPU или software adapter не подтверждает весь парк; результат без доступного устройства отмечается NotQualified.

## Приёмка и производительность

Обязательные проверки: positive/negative Roslyn fixtures; транзитивные helper calls; IR verification; независимые arithmetic golden vectors; bindings/layout roundtrip; CPU/GPU state equality; events/commands/lifecycle; startup fallback и faults; backward compatibility legacy API. Подробная матрица и объём прогонов находятся в [плане](IMPLEMENTATION_PLAN.ru.md).

Минимальный игровой shared-code corpus включает Movement, TargetRead, ThermalInventory, ShotBurstCapacity, MultiHitDamage с ordered apply и CarrierCapacity. Он проверяет inline inventory, extra own/optional access, ресурсы, RNG, разные domain keys, shared limits и tickets. Отдельные capability fixtures проверяют spatial queries, parent/turret/docking, service continuation и host lifecycle. Все варианты используют один source digest; RequireGpu фиксирует фактический executor kernels, а объявленные common CPU preparation/apply/service отображаются отдельно. Один Movement остаётся compiler smoke, но не закрывает gameplay приёмку.

Нужны тесты намеренной порчи reflection, смещения bindings на 1, неправильных stride/offset, несовместимых compiler/schema versions, потери device и числовой ошибки. Такие случаи должны обнаруживаться, а не давать тихо неверное состояние.

Производительность измеряется после корректности: CPU scalar/generated/parallel, GPU emulated/native и Auto на одинаковых входах. Собираются tick median/P95/P99, throughput, packing/upload/readback/dispatch/merge/commit, RAM/VRAM, allocations и на мобилках устойчивое время/нагрев. Порог выгодного batch size выбирается по измерениям. Универсальное ускорение или конкретный множитель до замеров не являются условием корректности; если GPU проигрывает, отчёт сохраняет результат и Auto может выбрать CPU на границе тика.

## Результаты разработки и границы работ

Планируемые модули: общие контракты и capability/execution options внутри SECS; отдельные SECS.GA.Compiler и SECS.GA.Analyzers для build; общий SECS.GA runtime и adapters по API; SECS.GA.Tests/Conformance и независимый oracle; tools/ga для локальной сборки bundles и сравнения traces. Разделение на пакеты фиксируется после технических исследований, с возможностью подключить обычный SECS без GPU/native dependencies.

У каждого этапа обновляются supported subset registry, diagnostics, guideline, artifact schema и qualification inventory. Значимые изменения правил требуют версии и migration note. Одно расширение языка считается готовым только вместе с CPU lowering, GPU lowering либо явным fallback, tests и документацией.

Реализация меняет только текущий проект, сохраняет чужие изменения, использует локальные caches и не выполняет push, запуск удалённых jobs, публикацию или изменения Miro. Текущий пакет является ТЗ и планом; реализация GA начнётся отдельными задачами по указанным критериям.
