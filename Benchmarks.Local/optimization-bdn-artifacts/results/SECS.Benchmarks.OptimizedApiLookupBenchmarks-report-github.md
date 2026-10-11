```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.204
  [Host] : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=Dry  Toolchain=InProcessEmitToolchain  IterationCount=1  
LaunchCount=1  RunStrategy=ColdStart  UnrollFactor=1  
WarmupCount=1  

```
| Method            | EntityCount | Mean      | Error | Ratio | Allocated | Alloc Ratio |
|------------------ |------------ |----------:|------:|------:|----------:|------------:|
| **WorldGet**          | **100000**      |  **2.824 ms** |    **NA** |  **1.00** |     **688 B** |        **1.00** |
| CachedAccessorGet | 100000      |  1.476 ms |    NA |  0.52 |     688 B |        1.00 |
|                   |             |           |       |       |           |             |
| **WorldGet**          | **1000000**     | **52.011 ms** |    **NA** |  **1.00** |     **688 B** |        **1.00** |
| CachedAccessorGet | 1000000     | 29.947 ms |    NA |  0.58 |     688 B |        1.00 |
