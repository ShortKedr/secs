[English](SUPPLEMENT.md) | Русский

# Дополнительные измерения SECS

Все значения относятся к текущим локальным исходникам и этой машине. Полные операции измерены в миллисекундах. Это отдельные нагрузки; таблицы не создают общего рейтинга API и систем.

Сырые данные: [API](optimized-api.json), [storage](storage.json), [BDN SECS](bdn-secs.json), [BDN legacy](bdn-legacy.json).

[Метаданные кампании](supplement.json) фиксируют ревизию, исходники и хеши входных данных. [Сравнение пяти ECS](REPORT.ru.md) — отдельная общая нагрузка.

## API

Восемь замеров на кейс. Создание: новый мир за повтор; подготовка, проверка, освобождение и принудительный GC вне таймера. Lookup использует прогретый мир и общий seeded порядок. Цвет сравнивает только методы одной операции при одинаковом N, по исходной точности; он не доказывает статистическую значимость.

### Create

| N | EmptyAddAdd | CreateEntityComponents | PreparedFactoryBatch |
| --- | --- | --- | --- |
| 100,000 | ![Отставание от 30%](../heatmap/worst.svg "Отставание от 30%") 10.028996 | ![Отставание между 5% и 30%](../heatmap/poor.svg "Отставание между 5% и 30%") 6.136315 | ![Отставание до 5%](../heatmap/best.svg "Отставание до 5%") 5.011138 |
| 1,000,000 | ![Отставание от 30%](../heatmap/worst.svg "Отставание от 30%") 105.423487 | ![Отставание от 30%](../heatmap/worst.svg "Отставание от 30%") 57.320200 | ![Отставание до 5%](../heatmap/best.svg "Отставание до 5%") 43.720438 |

### Lookup

| N | WorldGet | CachedAccessorGet |
| --- | --- | --- |
| 100,000 | ![Отставание от 30%](../heatmap/worst.svg "Отставание от 30%") 0.873819 | ![Отставание до 5%](../heatmap/best.svg "Отставание до 5%") 0.402610 |
| 1,000,000 | ![Отставание от 30%](../heatmap/worst.svg "Отставание от 30%") 44.408600 | ![Отставание до 5%](../heatmap/best.svg "Отставание до 5%") 19.960550 |

| Operation | Method | N | SD ms | Managed allocated bytes | Retained native bytes |
| --- | --- | --- | --- | --- | --- |
| Create | CreateEntityComponents | 100,000 | 0.115393 | 7154080.000 | 2402304 |
| Create | CreateEntityComponents | 1,000,000 | 0.812470 | 58933783.000 | 24004608 |
| Create | EmptyAddAdd | 100,000 | 0.265087 | 7159680.000 | 2405377 |
| Create | EmptyAddAdd | 1,000,000 | 5.135592 | 58939656.000 | 24007681 |
| Create | PreparedFactoryBatch | 100,000 | 0.132790 | 7154080.000 | 2402304 |
| Create | PreparedFactoryBatch | 1,000,000 | 0.632810 | 58934685.000 | 24004608 |
| Lookup | CachedAccessorGet | 100,000 | 0.007932 | 0.000 | 5245953 |
| Lookup | CachedAccessorGet | 1,000,000 | 1.547487 | 0.000 | 41946113 |
| Lookup | WorldGet | 100,000 | 0.015941 | 0.000 | 5245953 |
| Lookup | WorldGet | 1,000,000 | 4.387377 | 0.000 | 41946113 |

### Цветовая шкала

![Отставание до 5%](../heatmap/best.svg "Отставание до 5%") до 5% · ![Отставание между 5% и 30%](../heatmap/middle.svg "Отставание между 5% и 30%") 15% · ![Отставание от 30%](../heatmap/worst.svg "Отставание от 30%") от 30% · ![Практически неразличимо или недостаточно данных о разбросе](../heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../COLORS.ru.md).


## Storage

Не измерение времени: сохранённые нативные колонковые буферы после добавления/удаления payload 256 bytes, N=100000. Управляемые массивы, заголовки и overhead allocator исключены; значения не являются полной памятью процесса.

| Pass | Chunks | Native bytes |
| --- | --- | --- |
| 0 | 393 | 2405377 |
| 1 | 784 | 30432257 |
| 2 | 784 | 30432257 |
| 3 | 784 | 30432257 |
| 4 | 784 | 30432257 |
| 5 | 784 | 30432257 |
| 6 | 784 | 30432257 |
| 7 | 784 | 30432257 |
| 8 | 784 | 30432257 |
| 9 | 784 | 30432257 |
| 10 | 784 | 30432257 |
| 11 | 784 | 30432257 |
| 12 | 784 | 30432257 |
| 13 | 784 | 30432257 |
| 14 | 784 | 30432257 |
| 15 | 784 | 30432257 |
| 16 | 784 | 30432257 |
| 17 | 784 | 30432257 |
| 18 | 784 | 30432257 |
| 19 | 784 | 30432257 |
| 20 | 784 | 30432257 |

## BenchmarkDotNet

| Suite | Runtime | Configuration | Toolchain |
| --- | --- | --- | --- |
| SECS | .NET 8.0.31 (8.0.3126.42015) | Release | InProcess |
| Legacy | .NET 8.0.31 (8.0.3126.42015) | Release | InProcess |

Release, Short, InProcess: заданы 3 warmup и 8 actual итераций, вместо стандартных 3 actual у Short. Колонка actual / retained отделяет все собранные замеры от выборки Statistics после обработки выбросов. Setup/cleanup вне операций; естественный GC внутри включён. InProcess использует общий процесс, без изоляции child runtime. Allocation diagnostics не измеряют нативные колонки.

34 кейса SECS и 51 legacy кейс проверены по точным ключам. Здесь нет сопоставления разных операций: все ячейки нейтральны. MixedFrames — пакет 120 кадров. Padding 10 меняет долю совпадающих сущностей и не сравнивается с Padding 0 как ускорение одного workload.

Mutable-кейсы выполняют одну операцию за итерацию, чтобы сохранить корректный reset. Для многих BDN сообщает MinIterationTime ниже рекомендуемых 100 ms: восемь коротких наблюдений имеют ограниченную точность. Эти числа не заменяют длинный default-job и общую матрицу пяти ECS.

### SECS: 34

| Class | Method | N | Padding | Mean ms | SD ms | Actual / retained |
| --- | --- | --- | --- | --- | --- | --- |
| ComparisonBenchmarks | Churn | 1,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.084162 | 0.000181 | 8 / 7 |
| ComparisonBenchmarks | Churn | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 8.685446 | 0.031319 | 8 / 8 |
| ComparisonBenchmarks | Create | 1,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.092950 | 0.001973 | 8 / 8 |
| ComparisonBenchmarks | Create | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 9.231512 | 0.114356 | 8 / 8 |
| ComparisonBenchmarks | Destroy | 1,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.023088 | 0.000582 | 8 / 8 |
| ComparisonBenchmarks | Destroy | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 2.257686 | 0.056527 | 8 / 7 |
| ComparisonBenchmarks | Iterate | 1,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.000554 | 0.000005 | 8 / 7 |
| ComparisonBenchmarks | Iterate | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.057486 | 0.000507 | 8 / 8 |
| EcsBenchmarks | AddRemoveStructuralChurn | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 7.967103 | 0.028344 | 8 / 8 |
| EcsBenchmarks | LinearIteration | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.059448 | 0.001091 | 8 / 8 |
| OptimizedApiCreationBenchmarks | CreateEntityComponents | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 5.788525 | 0.187791 | 8 / 8 |
| OptimizedApiCreationBenchmarks | CreateEntityComponents | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 55.487662 | 0.728415 | 8 / 8 |
| OptimizedApiCreationBenchmarks | EmptyAddAdd | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 9.505175 | 0.091257 | 8 / 8 |
| OptimizedApiCreationBenchmarks | EmptyAddAdd | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 98.654512 | 1.290200 | 8 / 8 |
| OptimizedApiCreationBenchmarks | PreparedFactoryBatch | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.922357 | 0.082476 | 8 / 7 |
| OptimizedApiCreationBenchmarks | PreparedFactoryBatch | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 39.478525 | 0.463827 | 8 / 8 |
| OptimizedApiLookupBenchmarks | CachedAccessorGet | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.411067 | 0.005254 | 8 / 6 |
| OptimizedApiLookupBenchmarks | CachedAccessorGet | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 22.713771 | 1.907359 | 8 / 7 |
| OptimizedApiLookupBenchmarks | WorldGet | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.862012 | 0.022380 | 8 / 8 |
| OptimizedApiLookupBenchmarks | WorldGet | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 48.848557 | 4.257807 | 8 / 7 |
| StressBenchmarks | DenseIteration | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.086695 | 0.000443 | 8 / 8 |
| StressBenchmarks | DenseIteration | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 1.717920 | 0.030902 | 8 / 8 |
| StressBenchmarks | MixedFrames | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 191.419962 | 2.655826 | 8 / 8 |
| StressBenchmarks | MixedFrames | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 4726.987388 | 84.540038 | 8 / 8 |
| StressBenchmarks | PayloadChurn | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 36.570424 | 0.971818 | 8 / 8 |
| StressBenchmarks | PayloadChurn | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 629.639487 | 8.494460 | 8 / 8 |
| StressBenchmarks | RandomLookup | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.863942 | 0.011173 | 8 / 8 |
| StressBenchmarks | RandomLookup | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 42.385934 | 1.417310 | 8 / 8 |
| StressBenchmarks | Recycle10Percent | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.589744 | 0.022813 | 8 / 8 |
| StressBenchmarks | Recycle10Percent | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 64.008457 | 0.588117 | 8 / 7 |
| StressBenchmarks | SparseIteration | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.001578 | 0.000018 | 8 / 7 |
| StressBenchmarks | SparseIteration | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.007418 | 0.000024 | 8 / 8 |
| StressBenchmarks | WideIteration | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.182621 | 0.001609 | 8 / 8 |
| StressBenchmarks | WideIteration | 1,000,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 7.854919 | 0.116396 | 8 / 8 |

### Legacy: 51

| Class | Method | N | Padding | Mean ms | SD ms | Actual / retained |
| --- | --- | --- | --- | --- | --- | --- |
| Add1Component | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.639343 | 0.007922 | 8 / 7 |
| Add1ComponentRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 4.930314 | 0.103509 | 8 / 7 |
| Add1RandomComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 4.869743 | 0.079501 | 8 / 7 |
| Add1RandomComponentRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 7.367600 | 0.110373 | 8 / 7 |
| Add2Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 6.241829 | 0.064248 | 8 / 7 |
| Add2ComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 8.094971 | 0.155590 | 8 / 7 |
| Add2RandomComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 9.177843 | 0.581534 | 8 / 7 |
| Add2RandomComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 11.081200 | 0.221490 | 8 / 7 |
| Add3Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 10.091200 | 0.193362 | 8 / 7 |
| Add3ComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 12.134457 | 0.325350 | 8 / 7 |
| Add3RandomComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 13.104437 | 0.201225 | 8 / 8 |
| Add3RandomComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 15.367000 | 0.743967 | 8 / 8 |
| Add4Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 12.998988 | 0.175373 | 8 / 8 |
| Add4ComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 15.457375 | 0.656816 | 8 / 8 |
| CreateEmptyEntity | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 2.176871 | 0.026838 | 8 / 7 |
| CreateEntityWith1Component | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 5.871100 | 0.205427 | 8 / 7 |
| CreateEntityWith1RandomComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 6.299157 | 0.287739 | 8 / 7 |
| CreateEntityWith2Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 8.937088 | 0.208424 | 8 / 8 |
| CreateEntityWith2RandomComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 10.014313 | 1.025235 | 8 / 8 |
| CreateEntityWith3Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 12.328250 | 0.161232 | 8 / 8 |
| CreateEntityWith3RandomComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 12.089133 | 0.054311 | 8 / 6 |
| CreateEntityWith4Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 15.348971 | 0.571967 | 8 / 7 |
| DeleteEntity | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 1.463800 | 0.010116 | 8 / 7 |
| FilterMismatchSystems | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.018888 | 0.003309 | 8 / 8 |
| FourRemoveOneComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 4.233829 | 0.089746 | 8 / 7 |
| FourRemoveThreeComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 9.488762 | 0.535794 | 8 / 8 |
| FourRemoveTwoComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 6.395914 | 0.284841 | 8 / 7 |
| MultiSystems | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.054345 | 0.000970 | 8 / 8 |
| OneAddOneComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.840800 | 0.299594 | 8 / 8 |
| OneAddThreeComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 10.334362 | 0.260723 | 8 / 8 |
| OneAddTwoComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 7.167286 | 0.166894 | 8 / 7 |
| Remove1Component | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 2.987800 | 0.081176 | 8 / 7 |
| Remove1ComponentRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 4.877088 | 0.227712 | 8 / 8 |
| Remove2Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 5.722475 | 0.163954 | 8 / 8 |
| Remove2ComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 8.023486 | 0.255817 | 8 / 7 |
| Remove3Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 8.959912 | 0.197168 | 8 / 8 |
| Remove3ComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 11.283050 | 0.641939 | 8 / 8 |
| Remove4Components | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 11.506087 | 0.281150 | 8 / 8 |
| Remove4ComponentsRandomOrder | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 13.928483 | 0.150906 | 8 / 6 |
| SystemWith1Component | Run | 100,000 | 0 | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.032079 | 0.000466 | 8 / 7 |
| SystemWith1Component | Run | 100,000 | 10 | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.002958 | 0.000014 | 8 / 8 |
| SystemWith2Components | Run | 100,000 | 0 | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.035237 | 0.000786 | 8 / 8 |
| SystemWith2Components | Run | 100,000 | 10 | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.003206 | 0.000037 | 8 / 8 |
| SystemWith3Components | Run | 100,000 | 0 | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.049709 | 0.000713 | 8 / 8 |
| SystemWith3Components | Run | 100,000 | 10 | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 0.004299 | 0.000046 | 8 / 8 |
| ThreeAddOneComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 4.281975 | 0.107741 | 8 / 8 |
| ThreeRemoveOneComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.595613 | 0.191137 | 8 / 8 |
| ThreeRemoveTwoComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 6.308979 | 0.196507 | 8 / 7 |
| TwoAddOneComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.851000 | 0.104489 | 8 / 7 |
| TwoAddTwoComponents | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 7.629250 | 0.168171 | 8 / 8 |
| TwoRemoveOneComponent | Run | 100,000 | — | ![Нейтрально](../heatmap/equal.svg "Нейтрально") 3.464471 | 0.037152 | 8 / 7 |
