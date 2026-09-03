# <img src="assets/zoomies.png" alt="Zoomies logo" height="40"> Zoomies Chess Engine

Zoomies is a multi-threaded UCI chess engine with NNUE evaluation written with C#.

Zoomies Versions
| Version | Evaluation | Estimated Blitz Rating |
| -------- | -------- | -------- |
| v1.0 | HCE | ~2400 |
| v2.0 | NNUE | ~2800 |
| v3.0 | NNUE | ~3300 |
| v4.0 | NNUE | ~3500 |
| v5.0 | NNUE | ~3600 |

## Requirements
* Building from source: [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Running a [release](https://github.com/CodeAdopter/Zoomies/releases): .NET 10 Runtime

## Support
- UCI
- FRC

## NNUE
Zoomies evaluates with a single perspective NNUE

| Layer | Shape |
| -------- | -------- |
| Input features | 768 per king bucket x 8 king buckets |
| Feature transformer | 6144 -> 1024 per perspective, int16 weights and biases |
| Activation | pairwise CReLU, 512 activations per perspective |
| Output | 16 buckets of 1024 -> 1, int16 weights, int32 bias |

## Training
Zoomies nets are trained using Kennel, a custom NNUE trainer built with ILGPU 
that executes CUDA training kernels compiled from C#.

## Data
Zoomies training data was generated using
- Self play (1.3 billion)
- Imp GPU Forward Pass Distillation (100 billion)

## Imp (Parent Engine)
Imp is the parent engine of Zoomies written in c# with a much larger network 
trained on 3 billion self play positions

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

## Acknowledgments
- [Cutechess] (earlier testing)
- [Fastchess] (standard testing)
- [ILGPU] (training backbone)
- [Stockfish] (perft count validation)
- [PeSTO] by Ronald Friederich (fallback evaluation tables)
- Various Engines/Authors for engine opponents ([Petrel], [Stockfish], [Stash], [Stormphrax], [Lizard])
- [Chess Programming Wiki]
- [CCRL] for browsing open source engines
- UHO opening book by Stefan Pohl

[Cutechess]: https://github.com/cutechess/cutechess
[Fastchess]: https://github.com/Disservin/fastchess
[ILGPU]: https://github.com/m4rs-mt/ILGPU
[Stockfish]: https://github.com/official-stockfish/Stockfish
[PeSTO]: https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
[Petrel]: https://github.com/AleksPeshkov/petrel
[Stash]: https://github.com/mhouppin/stash-bot
[Stormphrax]: https://github.com/Ciekce/Stormphrax
[Lizard]: https://github.com/liamt19/Lizard
[Chess Programming Wiki]: https://chessprogramming.org/
[CCRL]: https://computerchess.org.uk/ccrl/
