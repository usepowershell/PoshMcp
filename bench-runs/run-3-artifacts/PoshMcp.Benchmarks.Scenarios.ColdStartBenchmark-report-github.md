```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8328)
Unknown processor
.NET SDK 10.0.202
  [Host]   : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  MaxIterationCount=10  MinIterationCount=3  
UnrollFactor=1  WarmupCount=3  

```
| Method                                              | Mode        | Mean    | Error    | StdDev   | P99          | P95     |
|---------------------------------------------------- |------------ |--------:|---------:|---------:|-------------:|--------:|
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **Single**      | **5.403 s** | **0.2624 s** | **0.0144 s** | **5,417.913 ms** | **5.416 s** |
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **Pool**        | **5.881 s** | **0.7962 s** | **0.0436 s** | **5,929.122 ms** | **5.924 s** |
| **&#39;Cold start: ctor → start → first invoke → dispose&#39;** | **ProcessPool** | **5.803 s** | **0.3335 s** | **0.0183 s** | **5,821.786 ms** | **5.820 s** |
