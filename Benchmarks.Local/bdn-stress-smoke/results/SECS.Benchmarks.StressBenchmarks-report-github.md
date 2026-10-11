```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.204
  [Host] : .NET 8.0.31 (8.0.3126.42015), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=Dry  Toolchain=InProcessEmitToolchain  IterationCount=1  
LaunchCount=1  RunStrategy=ColdStart  UnrollFactor=1  
WarmupCount=1  

```
| Method           | EntityCount | Mean           | Error | Allocated  |
|----------------- |------------ |---------------:|------:|-----------:|
| **DenseIteration**   | **100000**      |     **1,351.1 μs** |    **NA** |       **1 KB** |
| SparseIteration  | 100000      |       560.5 μs |    NA |       1 KB |
| WideIteration    | 100000      |     2,371.7 μs |    NA |       1 KB |
| RandomLookup     | 100000      |    16,685.2 μs |    NA |       1 KB |
| PayloadChurn     | 100000      |   116,004.7 μs |    NA |    1089 KB |
| Recycle10Percent | 100000      |     7,882.8 μs |    NA |       1 KB |
| MixedFrames      | 100000      |   458,067.3 μs |    NA | 1026.48 KB |
| **DenseIteration**   | **1000000**     |     **2,702.5 μs** |    **NA** |       **1 KB** |
| SparseIteration  | 1000000     |       607.5 μs |    NA |       1 KB |
| WideIteration    | 1000000     |     9,395.6 μs |    NA |       1 KB |
| RandomLookup     | 1000000     |   127,584.5 μs |    NA |       1 KB |
| PayloadChurn     | 1000000     |   999,999.0 μs |    NA |   16253 KB |
| Recycle10Percent | 1000000     |    95,782.7 μs |    NA |  951.97 KB |
| MixedFrames      | 1000000     | 8,491,691.1 μs |    NA | 1530.88 KB |
