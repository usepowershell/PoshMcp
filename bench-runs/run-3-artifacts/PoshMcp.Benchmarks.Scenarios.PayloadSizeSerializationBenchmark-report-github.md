```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8328)
Unknown processor
.NET SDK 10.0.202
  [Host]   : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 10.0.6 (10.0.626.17701), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
MaxIterationCount=15  MinIterationCount=5  WarmupCount=3  

```
| Method                                                          | Mode        | PayloadBytes | Mean        | Error        | StdDev      | P99       | Median      | P95         | Gen0     | Gen1     | Gen2     | Allocated   |
|---------------------------------------------------------------- |------------ |------------- |------------:|-------------:|------------:|----------:|------------:|------------:|---------:|---------:|---------:|------------:|
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Single**      | **1024**         |    **286.3 μs** |     **267.3 μs** |    **14.65 μs** |  **0.297 ms** |    **292.4 μs** |    **296.5 μs** |        **-** |        **-** |        **-** |    **11.78 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Single**      | **16384**        |    **974.6 μs** |     **562.5 μs** |    **30.83 μs** |  **0.994 ms** |    **991.2 μs** |    **993.3 μs** |   **3.9063** |        **-** |        **-** |   **126.96 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Single**      | **262144**       | **19,822.2 μs** | **182,235.9 μs** | **9,988.96 μs** | **30.946 ms** | **15,363.2 μs** | **29,674.0 μs** |  **62.5000** |  **62.5000** |  **62.5000** |  **2486.01 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Single**      | **1048576**      | **57,208.6 μs** |  **58,725.6 μs** | **3,218.95 μs** | **59.355 ms** | **58,749.7 μs** | **59,305.6 μs** | **285.7143** | **285.7143** | **285.7143** | **16339.57 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Pool**        | **1024**         |  **1,667.5 μs** |   **3,737.7 μs** |   **204.88 μs** |  **1.808 ms** |  **1,761.2 μs** |  **1,804.0 μs** |        **-** |        **-** |        **-** |     **12.3 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Pool**        | **16384**        |  **2,475.0 μs** |   **5,246.1 μs** |   **287.56 μs** |  **2.702 ms** |  **2,567.2 μs** |  **2,691.4 μs** |   **3.9063** |        **-** |        **-** |   **124.87 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Pool**        | **262144**       | **19,115.3 μs** | **104,464.0 μs** | **5,726.02 μs** | **25.381 ms** | **17,223.9 μs** | **24,715.4 μs** |  **62.5000** |  **62.5000** |  **62.5000** |  **2459.68 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **Pool**        | **1048576**      | **51,358.4 μs** |  **49,854.8 μs** | **2,732.71 μs** | **53.976 ms** | **51,482.2 μs** | **53,772.7 μs** | **200.0000** | **200.0000** | **200.0000** | **13786.75 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **ProcessPool** | **1024**         |    **536.8 μs** |   **1,777.9 μs** |    **97.45 μs** |  **0.646 ms** |    **487.4 μs** |    **632.9 μs** |        **-** |        **-** |        **-** |    **12.25 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **ProcessPool** | **16384**        |  **1,128.1 μs** |     **413.8 μs** |    **22.68 μs** |  **1.151 ms** |  **1,126.1 μs** |  **1,149.1 μs** |   **3.9063** |        **-** |        **-** |    **125.5 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **ProcessPool** | **262144**       | **14,400.0 μs** |  **31,677.0 μs** | **1,736.32 μs** | **16.345 ms** | **13,415.4 μs** | **16,105.9 μs** |  **62.5000** |  **62.5000** |  **62.5000** |  **2830.12 KB** |
| **&#39;Round-trip a string of size PayloadBytes through Write-Output&#39;** | **ProcessPool** | **1048576**      | **55,332.0 μs** |  **34,508.4 μs** | **1,891.52 μs** | **57.051 ms** | **55,590.0 μs** | **56,932.2 μs** | **375.0000** | **375.0000** | **375.0000** | **17363.35 KB** |
