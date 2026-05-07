```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8328)
Unknown processor
.NET SDK 10.0.202
  [Host]  : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD
  LongRun : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD

Job=LongRun  IterationCount=100  LaunchCount=3  
MaxIterationCount=20  MinIterationCount=5  WarmupCount=15  

```
| Method                                                 | Mode        | Concurrency | Mean     | Error   | StdDev  | P99        | Median   | P95      | Allocated |
|------------------------------------------------------- |------------ |------------ |---------:|--------:|--------:|-----------:|---------:|---------:|----------:|
| **&#39;Warm invoke @ N concurrency (network-shaped, 4× bar)&#39;** | **Single**      | **10**          | **664.4 ms** | **1.51 ms** | **7.81 ms** | **682.438 ms** | **662.0 ms** | **677.0 ms** | **205.32 KB** |
| **&#39;Warm invoke @ N concurrency (network-shaped, 4× bar)&#39;** | **Pool**        | **10**          | **136.4 ms** | **0.64 ms** | **3.26 ms** | **145.277 ms** | **135.8 ms** | **143.2 ms** | **200.09 KB** |
| **&#39;Warm invoke @ N concurrency (network-shaped, 4× bar)&#39;** | **ProcessPool** | **10**          | **205.4 ms** | **0.96 ms** | **4.83 ms** | **219.077 ms** | **203.8 ms** | **214.8 ms** | **203.81 KB** |
