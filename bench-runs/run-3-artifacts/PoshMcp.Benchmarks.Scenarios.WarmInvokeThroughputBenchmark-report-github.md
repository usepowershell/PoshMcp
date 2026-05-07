```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8328)
Unknown processor
.NET SDK 10.0.202
  [Host]   : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
MaxIterationCount=20  MinIterationCount=5  WarmupCount=3  

```
| Method                                                 | Mode        | Concurrency | Mean     | Error     | StdDev   | P99        | P95      | Allocated |
|------------------------------------------------------- |------------ |------------ |---------:|----------:|---------:|-----------:|---------:|----------:|
| **&#39;Warm invoke @ N concurrency (network-shaped, 4× bar)&#39;** | **Single**      | **10**          | **661.2 ms** | **409.32 ms** | **22.44 ms** | **686.233 ms** | **683.4 ms** |    **236 KB** |
| **&#39;Warm invoke @ N concurrency (network-shaped, 4× bar)&#39;** | **Pool**        | **10**          | **136.2 ms** | **115.60 ms** |  **6.34 ms** | **143.321 ms** | **142.5 ms** | **231.29 KB** |
| **&#39;Warm invoke @ N concurrency (network-shaped, 4× bar)&#39;** | **ProcessPool** | **10**          | **200.7 ms** |  **20.23 ms** |  **1.11 ms** | **201.406 ms** | **201.4 ms** | **252.02 KB** |
