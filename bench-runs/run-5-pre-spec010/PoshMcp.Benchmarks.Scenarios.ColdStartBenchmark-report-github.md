```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
Unknown processor
.NET SDK 10.0.107
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  Job-HAHJEI : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2

InvocationCount=1  MaxIterationCount=10  MinIterationCount=3  
UnrollFactor=1  

```
| Method                                              | Mode        | Mean    | Error    | StdDev   | P99          | P95     |
|---------------------------------------------------- |------------ |--------:|---------:|---------:|-------------:|--------:|
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **Single**      | **5.790 s** | **0.0732 s** | **0.0040 s** | **5,793.955 ms** | **5.794 s** |
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **Pool**        | **5.784 s** | **0.0847 s** | **0.0220 s** | **5,815.280 ms** | **5.812 s** |
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **ProcessPool** | **6.996 s** | **0.1801 s** | **0.0942 s** | **7,048.350 ms** | **7.046 s** |
