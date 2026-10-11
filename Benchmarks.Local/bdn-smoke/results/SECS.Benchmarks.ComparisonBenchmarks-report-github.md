```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.204
  [Host] : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method | EntityCount | Mean | Error |
|------- |------------ |-----:|------:|
| Create | 1000        |   NA |    NA |

Benchmarks with issues:
  ComparisonBenchmarks.Create: Dry(IterationCount=1, LaunchCount=1, RunStrategy=ColdStart, UnrollFactor=1, WarmupCount=1) [EntityCount=1000]
