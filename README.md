# Zoomies 1.0

Zoomies is a compact, single-threaded UCI chess engine.

Zoomes Versions
| Version | Rating |
| -------- | -------- |
| v1.0 | ~2000 |
| ? | ? |

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
