```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.204
  [Host] : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=Dry  Toolchain=InProcessEmitToolchain  IterationCount=1  
LaunchCount=1  RunStrategy=ColdStart  UnrollFactor=1  
WarmupCount=1  

```
| Method                 | EntityCount | Mean      | Error | Ratio | Allocated | Alloc Ratio |
|----------------------- |------------ |----------:|------:|------:|----------:|------------:|
| **EmptyAddAdd**            | **100000**      | **16.425 ms** |    **NA** |  **1.00** |   **6.83 MB** |        **1.00** |
| CreateEntityComponents | 100000      |  6.805 ms |    NA |  0.41 |   6.82 MB |        1.00 |
| PreparedFactoryBatch   | 100000      |  5.544 ms |    NA |  0.34 |   6.82 MB |        1.00 |
|                        |             |           |       |       |           |             |
| **EmptyAddAdd**            | **1000000**     | **98.625 ms** |    **NA** |  **1.00** |  **56.21 MB** |        **1.00** |
| CreateEntityComponents | 1000000     | 57.643 ms |    NA |  0.58 |   56.2 MB |        1.00 |
| PreparedFactoryBatch   | 1000000     | 41.806 ms |    NA |  0.42 |   56.2 MB |        1.00 |
