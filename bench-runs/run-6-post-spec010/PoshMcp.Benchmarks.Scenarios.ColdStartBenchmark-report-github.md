```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8390)
Unknown processor
.NET SDK 10.0.108
  [Host]     : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2
  Job-LQYLMB : .NET 10.0.8 (10.0.826.23019), X64 RyuJIT AVX2

InvocationCount=1  MaxIterationCount=10  MinIterationCount=3  
UnrollFactor=1  

```
| Method                                              | Mode        | Mean    | Error    | StdDev   | P99          | P95     |
|---------------------------------------------------- |------------ |--------:|---------:|---------:|-------------:|--------:|
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **Single**      | **6.470 s** | **0.1166 s** | **0.0610 s** | **6,569.304 ms** | **6.557 s** |
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **Pool**        | **6.483 s** | **0.1102 s** | **0.0393 s** | **6,528.059 ms** | **6.526 s** |
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **ProcessPool** | **7.742 s** | **0.1748 s** | **0.1156 s** | **7,916.422 ms** | **7.875 s** |
