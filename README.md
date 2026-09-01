# <img src="assets/zoomies.png" alt="Zoomies logo" height="40"> Zoomies Chess Engine

Zoomies is a multi-threaded UCI chess engine with NNUE evaluation written with C#.

Zoomies Versions
| Version | Evaluation | Estimated Blitz Rating |
| -------- | -------- | -------- |
| v1.0 | HCE | ~2400 |
| v2.0 | NNUE | ~2800 |
| v3.0 | NNUE | ~3300 |
| v4.0 | NNUE | ~3500 |
| v5.0 | NNUE | ~? |

## Requirements
* Building from source: [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Running a [release](https://github.com/CodeAdopter/Zoomies/releases): .NET 10 Runtime

## Support
- UCI
- FRC

## Build
```text
cd src
dotnet build -c release
```

## Run

```text
dotnet run --project src/Zoomies.csproj
```

The engine reads UCI commands from standard input.

## Diagnostics

```text
dotnet run --project src/Zoomies.csproj -- bench [depth]
dotnet run --project src/Zoomies.csproj -- ttd [movetime-ms] [hash-mb]
dotnet run --project src/Zoomies.csproj -- perft
dotnet run --project src/Zoomies.csproj -- frcperft
```

`bench` searches a fixed set of positions.
`ttd` measures time-to-depth on 15 positions.
`perft` validates legal move generation.
`frcperft` validates legal move generation for FRC positions.
