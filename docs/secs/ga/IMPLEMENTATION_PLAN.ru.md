# План реализации GA SECS

Дата: 12 октября 2026 года. Версия: 0.2, уточнена по [исследованию SpaceGame](SPACEGAME_REVIEW.ru.md). Статус: план разработки. [Техническое задание](SPEC.ru.md) задаёт обязательные контракты; [guideline](GUIDELINES.ru.md) описывает C# subset и диагностику. Все указанные новые модули, команды и API предстоит реализовать.

Цель первого прототипа — один исходник системы, CPU и настоящий GPU, точное совпадение состояния. Цель desktop выпуска — общий runtime и проверка Windows, Linux, macOS. Полный целевой выпуск дополнительно квалифицирует Android/iOS и заявленные API. Один Vulkan prototype не закрывает весь GA scope.

## Зависимости с текущими работами

| Зависимость | Что использовать | Что GA добавляет |
| --- | --- | --- |
| [Fixed](../prompts/03_FIXED.ru.md) | Q32.32, rounding/overflow, scalar API, golden vectors и BigInteger oracle | Intrinsic mapping, точный wide lowering, fault protocol и стоимость на GPU |
| [Runner и time slicing](../prompts/02_TIME_SLICING.ru.md) | Logical tasks, ownership, Pending/Completed/Faulted, commands и barriers | Device submissions, ожидание fences, buffer lifecycle и CPU/GPU boundaries |
| [CPU parallel](../prompts/04_MULTITHREADING.ru.md) | Тот же план и merge при разных workers | Executor conformance без второго scheduler |
| [CPU SIMD](../prompts/01_SIMD.ru.md) | Exact kernels и scalar fallback | Только доказанно эквивалентные оптимизации generated CPU path |
| [Determinism runner](../../SECS_DETERMINISM_PLAN.ru.md) | Canonical schema, replay traces, независимый oracle, comparer | GPU/device metadata, поэтапные checkpoints и RequireGpu проверки |

Не ждать завершения всей математики для исследования frontend/bindings. Сначала можно использовать int/uint. Fixed GPU приёмка зависит от зафиксированного числового контракта и его tests. Реализация lifecycle и core World принадлежит общей ветке runner; GA не переписывает её параллельно.

## Этапы и проверяемые задачи

### GA 00 Контракты и интеграция

Зафиксировать public API sketch, subset v1, числовую версию, stable schema/SystemId, доступы, faults и fallback policies. Сопоставить каждое правило с текущим runner планом. Создать таблицу владения файлами и списка зависимостей от соседней работы. Утвердить формат событий/команд и пакета artifacts до backend реализации.

По SpaceGame здесь же определить SystemContext: дополнительные own-компоненты, optional Has/TryRead, generation-aware neighbor snapshots, tick resources, ReadBuffer и contributions. Зафиксировать source/stream domain order descriptors, collision-proof sourceKind keys и типы стадий EntityKernel/SnapshotPreparation/OrderedApply/CpuService. Описать CommitGroup нескольких producers с общим stream, snapshot epochs/data barriers и commit после группы; standalone default общего runner сохраняется. Описать nested InlineArray[16]/SquadMembers[4] schema и проверку required own-компонента без скрытого изменения query membership. Эти решения предшествуют реализации frontend, а не откладываются целиком до GA 08.

Результат: contract fixtures и схема metadata, по которым независимые CPU и GPU исполнители получают одни и те же ожидаемые значения. Приёмка: нет конкурирующих Fixed типов, scheduler или различий ownership; legacy поведение явно сохранено. Решения, требующие исследования, перечислены с владельцем и тестом, а не скрыты в API.

### GA 01 Исследование Slang и runtime

Выбрать закреплённую Slang version и downstream toolchains. На Windows/D3D12, Linux/Vulkan и macOS/Metal собрать один диагностический kernel из общих данных, извлечь target reflection и создать native pipeline. Этот внутренний fixture может иметь shader source; ручные shaders не становятся API автора игры.

Проверить implicit parameter containers, отдельные resource namespaces, bindings с началом 0/1, arrays, nested structs, uniform/storage layouts, stride/alignment и unused resources. Проверить различие declared layout и per-entry usage после emission: отсутствие usage нельзя трактовать как использование или удаление. Сравнить Compilation API и CLI reflection JSON на выбранной версии; использовать API по умолчанию, CLI только при достаточной проверенной schema не ниже 1.1 с scope/container bindings. При необходимости получить usage через API. Compiler configuration и profile IDs сохранять устойчивыми именами/значениями; не полагаться на нестабильные внутренние номера.

Результат: решение о toolchain, матрица его фактических возможностей, пример SECS bundle/manifest и точный adapter контракт. Приёмка: upload → dispatch → readback совпадает на трёх desktop ОС, binding permutation не меняет значение, испорченный manifest отвергается. Недоступная ОС отмечается NotQualified и оставляет этап неполностью принятым. Для исследуемых GL/GLES/WebGPU маршрутов отдельно записать feasibility и стоимость адаптации.

### GA 02 Roslyn frontend и анализатор

Создать GA opt-in build property, marker системы, semantic analysis и diagnostic IDs. Начать с примитивных полей, структур, enum, local variables, if и loops; добавить проверку ref/in доступа. Анализировать достижимые helpers, operators/properties и известные generic specializations. Unknown library вызов не пропускать.

Классифицировать portable, детерминированный CpuOnly lowering и invalid contract. Сохранять решения в build report; GPU-specific diagnostics включать при GA build, а не при обнаружении GPU. RequireGpu policy должна превращать необходимый CPU fallback compute системы в отказ выбора, без ложного успеха. Пример managed Battle reference диагностируется с цепочкой доступа; проверенный SystemContext lowering показывает путь без closure. InlineArray, nested fields, own extra-component ref, optional tags и integer enum switch получают отдельные positive/negative cases.

Результат: IDE/build diagnostics с source span и explain/fix. Приёмка: positive и negative fixtures для каждой конструкции; один неправильный вызов внутри helper обнаруживается; CPU-only сборка обычного SECS не зависит от Slang. Неподдержанный детерминированный lowering выдаёт объяснимый fallback, Random/time/races — ошибки.

### GA 03 IR и эталонное CPU исполнение

Создать typed IR, verifier, source map и schema/intrinsic registry. Представить арифметику, conversions, control flow, component reads/writes, checks, commands и faults. Generator выдаёт registration/adapters. CPU lowering использует тот же IR; original C# debugging route допускается после проверки эквивалентности.

Интегрировать CPU plan в общий runner, сохранив canonical tasks и commands. В качестве эталона использовать независимый managed interpreter/fixture oracle; сравнение двух исполнителей одного неверного IR не является достаточным доказательством. Здесь же проверить CPU версии шести gameplay fixtures и declarations ресурсов, source order, nested inventory и ordered apply. Это эталон до GPU, не перенос нынешней float игры.

Приёмка: одно тело Movement выполняется в generated CPU и oracle; совпадают полный state и продолжение. Ref alias и неверный IR отклоняются. Изменение ChunkCapacity и worker count не меняет результат. Entity ID reuse при сохранении domain Serial не меняет tie choice. Snapshot preparation и declared CPU stages используют тот же runner, без второго Tick. Исходные legacy тесты остаются зелёными.

### GA 04 Детерминированные GPU числа

Lowering int/uint задаёт C# и target-independent rules для overflow, signed divide/remainder, shifts и conversions. Добавить Q32.32 представление из пары uint32 и wide arithmetic до 128 бит, с точными signs, rounding и fault checks. Не расширять публичный Fixed отдельным GPU типом.

Повторно использовать числовые golden vectors. Тестовый независимый oracle использует BigInteger; он не входит в hot path. Native wide optimization получает отдельную equivalence suite и capability marker. Если операция пока только CPU, это отражается в profile и diagnostics без изменения precision.

Приёмка: все значения и fault records совпадают с числовым контрактом: +/-halfway, ties-even, Min/Max, unary Min, divide by zero, overflow после округления и конверсии. Проверка включённого/выключенного C# checked контекста даёт одинаковые Fixed результаты. Две ошибочные операции, в том числе повторение в loop и fault разных сущностей/producers, выбирают один минимальный (tick, stage, systemOrder, kernelOrder, sourceOrderOrdinal, evaluationOrdinal) key при разных workers/groups в объявленном source order. Поведение Faulted runner подтверждено.

Отдельный последующий subprofile AdvancedGameMath исследует vector normalization, sqrt/trig/atan2/wrap, damping, intercept quadratic и conservative bounds из FireControl/CircleSweepMath. Сначала range audit и independent golden/oracle, затем intrinsic и CPU/GPU lowering. Tangency, zero/near-zero roots, discriminant tolerance, overflow и angle boundaries обязательны. Это dependency полной игровой qualification, а не скрытое расширение текущей задачи Fixed и не условие первого integer prototype.

### GA 05 Slang генерация и packaging

Lower IR в Slang, добавив проверенные intrinsics и source mapping. Сформировать отдельные artifacts по target/profile и manifest с reflection/usage. Для D3D зафиксировать downstream HLSL → DXIL/DXBC маршрут и проверить native pipeline; raw HLSL не считается готовым binary artifact. Build output хранит CPU plan даже при наличии GPU variants. Генерировать pack/unpack по schema и layout, а не копировать произвольные структуры целиком.

На этапе допускается выпуск сначала одного GPU target. Остальные target lanes должны быть сохранены в архитектуре packaging и плане. Если невозможно собрать нужный target, пакет CPU fallback остаётся корректным; обязательный RequireGpu build/qualification lane не проходит.

Приёмка: повторная сборка при закреплённых входах даёт тот же normalized manifest/content identity с отдельно описанными недетерминированными полями toolchain. Неправильные schema/options/digests отвергаются. Документированы local dependencies и лицензии. Mobile package не требует slangc на устройстве.

### GA 06 Startup выбор и backend интерфейс

Добавить CpuOnly/Auto/RequireGpu, detection adapters, capability profile registry, startup probe и structured report. Минимальный профиль обходится без int64/subgroup/широких atomics. Реализовать причины fallback, compatibility checks и заранее известные memory limits.

Первый adapter выбирается по реально доступному парку устройств: Vulkan или D3D12. Выбор фиксируется в отчёте GA 01; он не меняет общий контракт. Headless mode и отделение от renderer обязательны.

Приёмка: no device, wrong API/features, missing artifact, invalid manifest, pipeline failure и probe failure корректно выбирают CPU в Auto до тика; RequireGpu отказывается. CpuOnly не создаёт GPU resources. Предупреждение и фактический system plan доступны программе.

### GA 07 GPU исполнение и lifecycle

Подключить buffer allocation, uploads/readback, packing, submit, barriers/fences, drain и resource lifetime к общему runner. Start/Advance/Completed учитывают завершение dispatch и check faults. Сохранять стабильные logical jobs при разных physical groups и chunks. BeginTick входы загружаются один раз и не опрашиваются между срезами.

CPU/GPU boundary синхронизирует только нужные данные, но оптимизация не должна оставлять stale state. До реализации residency оптимизаций допустим простой точный readback. Snapshot preparation ресурсов и own/optional accesses поддерживаются обоими исполнителями. На BeginTick учитывать допустимые external ref edits: mutation counter только для Set недостаточен. Нельзя объявить acceleration по одному shader timing.

Приёмка: Movement из одного исходника реально выполняется на GPU и совпадает с CPU каждый тик; empty/tails/reallocated buffers/one entity проверены. Внешние обращения к World во время Pending отклоняются. Abort/device loss/dispose выполняют drain и общую Faulted политику; повторного скрытого CPU запуска тика нет. Добавить headless host fixture с несколькими Advance/Poll на тик и неизменным Completed snapshot; Capture/observer/checksum не читают live World в Pending.

### GA 08 Lookup события и structural commands

Добавить snapshot lookup с ID/generation checks; canonical event collection, reduction и ordered application, tickets и общий CPU commit. Для GPU events выбрать count/prefix/write или эквивалентную точную схему, учитывающую framework buffer overflow и localOrdinal. Source/stream order descriptors сохраняют необходимые serial/slot/team keys и sourceKind; проверить одинаковый ID у entity и logical job и одинаковые localOrdinals разных producers. CommitGroup объединяет несколько producer kernels до domain sorting/apply, не теряя заданной component visibility. Ticket references допускаются только внутри одного источника после Create, и domain descriptor не нарушает эту зависимость. Semantic game capacity rejection записывается отдельно от технического overflow. Не использовать atomic append position как semantic order.

Результат: шесть gameplay fixtures из таблицы ниже имеют по одному C# исходнику для исполнителей и независимый oracle. Приёмка: TargetRead проверяет stale/destroyed/reused generation; ThermalInventory — дополнительные own-компоненты, inline array, tags и counters; ShotBurstCapacity — слоты, bursts, heat/лимит и tickets; MultiHitDamage — ordered shield/plasma/death/LastAttacker; CarrierCapacity — team/home/repair limits, отказ и порядок ID. Смена GPU completion order ничего из этого не меняет. Generic damage sum не подменяет последовательный fold.

Spatial/parent/docking capability получает отдельную ветку: typed flat buffers, CPU build/GPU consume, conservative candidate oracle, duplicate/unique policy и snapshot invalidation. Начальный lookup не равен полной spatial query поддержке. Exact CpuService admission и continuation schema проверяются на маленьком stateful сервисе; реальный float PhysicsService остаётся диагностическим legacy workload до числовой квалификации. Не вводить Aether fallback для другого production solver.

### GA 09 Три desktop платформы

Расширить и квалифицировать Windows/D3D12, Linux/Vulkan, macOS/Metal. Отдельно проверить headless execution и CPU fallback на каждой ОС. Собирать compiler, OS/CPU architecture, GPU/driver, API/profile и actual kernel executor. Варианты x64/ARM64 не объявлять проверенными по результатам эмуляции другой архитектуры. Desktop host integration lane проверяет Update/Render, pause/restart и last Completed frame через общий runner, а не только direct Battle.Step.

Приёмка: conformance corpus и binding suite проходят на реальном GPU каждой ОС; все traces равны одному независимому oracle. NVIDIA/AMD/Intel/Apple получают отдельные inventory строки. Desktop release нельзя закрыть только compilation tests либо одним производителем. Если точный Slang Metal route недостаточен, задача включает его настройку/адаптацию и оставляет квалификацию открытой до runtime pass.

### GA 10 Android и iOS

Выбрать mobile host/AOT integration без зависимости от CLR JIT. Собрать APK/app package с CPU plan и platform artifacts. Android: Vulkan и CPU, затем GLES route; iOS: Metal и CPU. Использование Unity/IL2CPP либо .NET mobile workload является отдельным подтверждённым host решением, не выводом из desktop net8.0.

Приёмка: реальный Android ARM64 и реальный iOS device исполняют тот же source и contract. Для заявленного Android охвата проверены Adreno и Mali; simulator не заменяет device. Lifecycle pause/resume, allocation limits и стабильность длительного прогона проверены. AOT fallback работает без compiler/сети. Оценить transfers, memory и thermal degradation без изменения числового профиля.

### GA 11 Дополнительные API и совместимость

Квалифицировать D3D11, Windows Vulkan, OpenGL compute, GLES compute и WebGPU по порядку пользовательского спроса и доступных устройств. Для GL/GLES требуется доказанный Slang или вспомогательный compiler route и собственный adapter; поддержка языка GLSL не равна готовому compute runtime. WebGPU требует WGSL route и host. Production [SpaceBattle.Web](../../../SpaceBattle.Web/SpaceBattle.Web.csproj) уже имеет поддерживаемый стабильный net8.0 Mono AOT CPU host: отдельная задача адаптирует его bridge к неблокирующему runner и versioned snapshot buffer, сохраняя CPU fallback. Локальная диагностика Mono interpreter поддерживается отдельно и не заменяет production baseline.

Каждый новый API получает полную bindings/layout, arithmetic, runner и fallback suite. Проверить feature profiles и downlevel устройства. Версии без требуемого compute остаются CPU-only.

Приёмка: qualification inventory содержит отдельный статус каждой пары OS/API/device. Ни один дополнительный API не считается принятым из-за успешного соседнего backend. Не поддерживаемый GPU профиль не блокирует Auto CPU работу устройства. Browser lane проверяет pause/hidden/reset, pending fence, CPU fallback, view lifetime при realloc и отсутствие чтения live World из JS diagnostics; wall-clock backlog cap не изменяет logical tick inputs.

### GA 12 Оптимизация и выпуск

Измерить полный tick cost на одинаковых workloads. Добавить GPU residency, batching, dirty range transfer, native-wide kernels и при необходимости system fusion только после semantic equivalence tests. Порог Auto выбирается до тика или на Completed границе; величины времени могут влиять на executor choice, но не на алгоритм и результат.

Подготовить SDK samples, versioned subset registry, guide migration notes, воспроизводимые команды и qualification inventory. Показать setup и steady-state отдельно. Сохранять проигравшие benchmark случаи. Профилировать девять реальных фаз и CPU/GPU service boundaries: acceleration particle kernel не доказывает выигрыш полного тика. Исторический Aether workload и текущий circle workload ведут отдельные baselines; старые timings не используются как измерение обновлённого source.

Приёмка: полный release gate ниже, измеренная стоимость на low/medium/high нагрузках, задокументированное покрытие и сохранение legacy потребителей. Отсутствие универсального GPU ускорения не маскируется; поддержка публикуется только в пределах qualification inventory.

## Порядок выполнения и параллельная работа

Основная цепочка: GA 00 → GA 02 → GA 03 → GA 05 → GA 06 → GA 07 → GA 08 → GA 09 → GA 10 → GA 11 → GA 12. GA 01 выполняется после GA 00 и завершает toolchain/runtime решения до GA 05/06. GA 04 зависит от принятого Fixed контракта и IR; integer GPU prototype может предшествовать полному Fixed lowering, но выпуск с Fixed — нет.

| Независимая работа | Владение | Условие интеграции |
| --- | --- | --- |
| Общий Fixed | Владелец существующей числовой задачи, SECS/Numerics и числовые tests | Версия/API/golden corpus передаются GA без параллельного переписывания |
| Roslyn и subset | GA compiler/analyzer разработчик, новые compiler проекты | Согласованный IR/API из GA 00 |
| Slang bindings исследование | Backend разработчик, tools/ga и fixtures | Общий manifest и logical resource schema |
| Runtime runner | Владелец общей runner ветки, World/Query/ownership | GA executor использует согласованные interfaces |
| Backend adapters | По одному владельцу API, отдельные adapter проекты | Один frozen runtime interface и conformance corpus |
| Тестовый oracle и docs | QA/интегратор, новые conformance/docs файлы | Не повторяет production arithmetic; проверяет contract |

Изменения World/Query/runner и math contracts интегрируются последовательно. Независимые backend adapters можно разрабатывать параллельно после GA 01/06. Главный интегратор проверяет итоговое поведение и не принимает только отчёт исполнителя. Точные оценки календарных сроков выдаются после GA 01 и вертикального прототипа GA 07: сейчас неизвестны стоимость Q32.32 lowering и доступность mobile/runtime маршрутов.

## Матрица проверки

| Suite | Минимальное содержание | Условие успеха |
| --- | --- | --- |
| Language | Каждая разрешённая конструкция и запрещённая форма; hidden helper/property/operator/generic/library call | Stable diagnostics и одинаковая classification при IDE/build |
| Arithmetic | Golden vectors Fixed, int/uint, случайные reproducible vectors, ошибки и rounding | Полный raw/fault match independent oracle; emulated/native равны |
| Storage | Nested structs, byte/bool/enum, padding, uniform/storage, strides, arrays и unused resources | Pack/unpack сохраняет все declared fields |
| Bindings | Индексы 0/1, gaps, multiple spaces, implicit globals, reflected entry points, target-specific namespaces | Настоящий dispatch читает/пишет правильные logical resources |
| Simulation | Шесть gameplay fixtures, RNG, semantic rejection, ordered apply и structural churn | Равенство всех system/tick snapshots, events и accepted/rejected requests |
| Context/resources | Extra own access, optional tags, InlineArray[16], frozen Config/Tick и counters | Нет скрытого managed closure; полный declared read/write set |
| Order | Serial против reused ID, shot slots, states enable/disable, build capacity и score ties | Domain key и полный tie-break не меняются от backend |
| Spatial | Candidate vs exact, duplicates vs unique, outside/large bounds, boundaries, generations, Seal/Add/Clear | Independent brute-force coverage; нет false negatives и stale caches |
| Continuation | Allocator/generations, resources, future queues, RNG, service history и body-slot/PhysicsLink mapping | Restore/continue совпадает; потерянная pair history либо неверный remap обнаруживаются |
| Scheduling | Chunk capacities 1/64/127/256, CPU workers 1/2/доступное N, несколько device-legal group sizes, Advance budgets 1/3/64 | Один logical source order, результат не зависит от physical layout |
| Startup | No GPU, no feature, no bundle, invalid layout/digest, failed pipeline/probe | Auto CPU корректен; RequireGpu не даёт ложный GPU pass |
| Lifecycle | Pending ownership, mixed plan, abort, arithmetic fault, device loss, dispose и restart from checkpoint при его наличии | Нет частичного Completed/повторных событий; ресурсы drained |
| Platform | Три desktop ОС и два mobile направления; фактические GPU families/API | Каждый заявленный профиль имеет device/runtime свидетельство |
| Compatibility | Старые SECS.Tests и builds legacy потребителей | Без GA поведение и dependency footprint сохранены |

Начальный воспроизводимый корпус: seeds 0, 1, 27, 7331, 4294967295 с определённым RNG поведением; entity counts 0, 1, 63, 64, 65, 127, 128, 129, 1024. Короткий local gate: все малые граничные случаи и шесть shared-code gameplay fixtures по 1000 тиков. Длительная qualification: те же шесть fixtures минимум по 100000 тиков и отдельный structural stress с create/destroy/reuse. Для больших наборов использовать per-system hashes с полным снимком первого расхождения и периодическими полными checkpoints; маленькие fixtures сравнивать полностью.

### Игровые fixtures из SpaceGame

| Fixture | Источник требований | Обязательная проверка |
| --- | --- | --- |
| SG 01 Movement | Debris/ParticleMotion | Независимый компонентный kernel, tails и точная integer/Fixed интеграция |
| SG 02 TargetRead | ProjectileMotion, entity snapshot table | ReadSnapshot при параллельной записи цели, Alive/Has, stale generation и reused ID |
| SG 03 ThermalInventory | Thermal/Inventory/Dock | Inline inventory, дополнительный own Shield/Heat, optional tags, несколько events и counters |
| SG 04 ShotBurstCapacity | TryFire/FlushShots, turrets | (Serial, Slot), burst/heat, accepted/rejected по MaxProjectiles, tickets, nested payload и RNG draws |
| SG 05 MultiHitDamage | ResolveHits/Damage | Ordered contacts/projectiles/debris, Shield/plasma/LastAttacker, duplicate death, Alive check после предыдущего hit |
| SG 06 CarrierCapacity | Carrier/FlushLifecycle | (Team, Order), team/home capacity, docking slots, отказ, commit visibility и ID allocation |
| SG 07 SpatialParent | TacticalAi, FlatGridLayout, mounted turrets | Candidate/exact policy, duplicate IDs, score ties, parent destruction/reparenting и snapshot refresh |
| SG 08 ServiceContinuation | PhysicsService и Checksum | Stateful exact CPU service, pair history и body-slot/PhysicsLink schema либо remap, multi-contact ordered solver после numerical qualification |
| SG 09 HostLifecycle | Headless/Game/Web loops | BeginTick/Advance/Poll/Completed, pause/reset/drain, immutable render frame и Web view lifetime |

SG 01–06 обязательны для gameplay acceptance desktop GA. SG 07/08 обязательны при заявлении соответствующих spatial/service возможностей и до полной игровой parity. SG 09 применяется к каждому объявленному host. Во всех переносимых fixtures используется новая exact numerical model, а исходная float игра остаётся отдельной regression baseline.

### Полная игровая квалификация

Для последующего перевода SpaceGame составить per-stage admission table: один source, declared context/resources/order, numeric profile, executor и coverage. Принять AdvancedGameMath, exact physics CPU service, complete continuation schema и host adapters. Затем сравнить полный тик CPU-only и mixed/GPU, включая VFX allocation dependencies, turrets, waves, death/respawn и long-run capacities. Текущая задача исследования не мигрирует эти исходники; готовность шести fixtures не выдаётся за готовность всей игры. CPU сервис без подтверждённого числового контракта блокирует full-game claim даже при ускорении остальных систем.

Это стартовый бюджет tests, а не измеренное время исполнения. При корректировке сохранить покрытие границ, длинного продолжения, seeds и ошибок, записав изменение corpus version. Unsupported group sizes помечать, а не принудительно запускать за пределами device limits.

GPU lane использует RequireGpu и сверяет actual executor telemetry. Автоматический CPU fallback делает GPU lane непрошедшим, даже если результаты верны. Headless и mobile probes — отдельные lanes. Software adapter, compilation-only и удалённый отчёт без artifacts не подменяют реальное device исполнение.

## Release gate и результаты

| Выпуск | Что должно быть принято |
| --- | --- |
| Вертикальный prototype | GA 00–07 на одном GPU API, one-source Movement, exact integer/заявленный Fixed профиль, startup fallback и fault semantics |
| Desktop GA | GA 08–09, SG 01–06 и headless/desktop SG 09, Windows/Linux/macOS runtime, versioned artifacts/guide |
| PC и mobile GA | GA 10, Android/iOS AOT и реальные devices, одинаковый source/contract и CPU fallback |
| Дополнительный API | Полная GA 11 suite и отдельный qualification status, без автоматического расширения чужого покрытия |

Пакет приёмки содержит source digest, contract/subset/IR/numeric versions, artifact/reflection digests, toolchain options, actual device/driver, CPU/GPU traces, первый diff при ошибке, warnings/fallback reasons и измерения. Смена compiler/runtime/driver с риском изменения поведения требует повторной qualification затронутого профиля. Сбой и неподдержанное устройство сохраняются как результат.

## Поддержка guideline и scope изменений

Каждое расширение языка или intrinsic одновременно обновляет registry, analyzer, IR verification/lowering, positive/negative fixtures, differential tests и GUIDELINES. Требование без CPU lowering не включается; GPU lowering может быть временно заменён только явно безопасным CPU fallback. Изменение результатов означает новую semantic version контракта, не тихое обновление backend.

Новые проекты и scripts создаются внутри текущего репозитория. Existing docs Fixed/runner и чужие изменения не переписываются без конкретного интеграционного diff. SpaceBattle миграция — отдельная задача; текущая float/double circle simulation не используется как доказательство нового контракта. Aether существует в study/reference и исторических workloads, а не в production Core. Само исследование и его источник решений сохранены в SPACEGAME_REVIEW.

Локальный workflow будущих задач: inspect status → изменить согласованные файлы → build/test с project-local caches → прочитать traces и проверить runtime → обновить документы/qualification. Добавление CI примеров возможно локально; push, запуск удалённых jobs, публикация и изменения настроек сервисов не выполняются в рамках действующих разрешений. Установка инструментов за пределами проекта требует отдельного разрешения с точным путём.
