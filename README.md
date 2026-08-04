# Zoomies Chess Engine

Zoomies is a compact, single-threaded UCI chess engine.

Zoomies Versions
| Version | Rating |
| -------- | -------- |
| v1.0 (HCE) | ~2000 |
| v1.18 (HCE) | ~2600 |
| v2.18 (NNUE) | ~3450 |

## Requirements
* Building from source: [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Running a [release](https://github.com/CodeAdopter/Zoomies/releases): .NET 10 Runtime

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
dotnet run --project src/Zoomies.csproj -- perft
```

`bench` searches a fixed set of positions.
`perft` validates legal move generation.
