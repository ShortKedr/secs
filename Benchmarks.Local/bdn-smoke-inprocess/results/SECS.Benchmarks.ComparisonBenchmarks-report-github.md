```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.204
  [Host] : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=Dry  Toolchain=InProcessEmitToolchain  IterationCount=1  
LaunchCount=1  RunStrategy=ColdStart  UnrollFactor=1  
WarmupCount=1  

```
| Method  | EntityCount | Mean        | Error | Allocated |
|-------- |------------ |------------:|------:|----------:|
| **Create**  | **1000**        |    **560.0 μs** |    **NA** |   **64768 B** |
| Iterate | 1000        |    329.1 μs |    NA |     688 B |
| Churn   | 1000        |  1,283.7 μs |    NA |   13984 B |
| Destroy | 1000        |    365.3 μs |    NA |    9080 B |
| **Create**  | **100000**      | **35,038.7 μs** |    **NA** | **7157648 B** |
| Iterate | 100000      |    827.5 μs |    NA |     688 B |
| Churn   | 100000      | 40,120.5 μs |    NA | 1722592 B |
| Destroy | 100000      |  4,656.8 μs |    NA | 1049632 B |
