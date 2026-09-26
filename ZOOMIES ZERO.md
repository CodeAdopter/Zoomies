version 6 of zoomies will drop nnue evaluation and go back to its original build philosophy and replaced with HCE version Zoomies Zero

> original philosophy: speed quantified by nodes per second

the problem with the current progression of zoomies
1. the goal drifted from speed to strength through over reliance on neural networks. strength still matters, just not first, otherwise version 1 at 50 mnps would have been the end point
2. nnue trades speed for elo. bigger nets and threat inputs cost a lot of nps, and much of the extra capacity goes into handling the imbalanced positions testing rewards
3. speed stops mattering. with a large net most of the time per node goes to the net, so performance work barely shows and all effort goes into the net
4. top engines converge. strong nnue engines agree on the best move almost everywhere and often differ by a single quiet move, so balanced games mostly end in draws
5. testing leans on unbalanced openings to avoid those draws. results then measure how well an engine converts the books imbalances
6. nnue work is heavy and dull. data generation and training eat disk space and time, search ideas that make up for a small net (like lmr features) get wiped out by the next bigger net, and everything ends up as net weights
7. progress becomes hard to read. you can see why a search idea works, a net just gets better or worse
8. the file size exploded with the weights each quality gain needs

## Zoomies Zero Builds

zoomies zero is a stripped down hand crafted version of zoomies focused on speed. the engine code compiles as is on top of a custom core library

- core types
- memory
- math and bits
- collections
- text and io
- system
- compile only stubs

the .net runtime and the JIT are both replaced by the Zero AOT Runtime. the standard native aot compiler builds the engine ahead of time against the custom core library, which carries a tiny runtime of its own. no compiler modifications are needed yet but still on the table, most of the work is focused on the zero AOT Runtime.

| part | vs Native AOT |
|---|---|
| runtime (gc, startup) | 150x smaller |
| base library | 75x smaller |
| type descriptors and metadata | 140x smaller |
| unwind tables, headers, imports | 60x smaller |
| static data and literals | 7x smaller |
| linq, process, tooling | removed (tools stay in the full build) |
| engine code | about the same (same compiler) |

size comparison of c# builds

| build | size | vs Zero AOT |
|---|---|---|
| embedded runtime | 73.7 mb | 430x larger |
| embedded runtime trimmed | 13.4 mb | 78x larger |
| Native AOT | 2.19 mb | 12.8x larger |
| Zero AOT | 168 kb | |

Zero AOT is still being prototyped but already matches Native AOT in speed. the embedded runtime builds keep a small lead since they still run the JIT

## Zoomies Zero Features/Goals

- self improving hce engine
- dynamic tuning
- experimental ideas
- speed first
- small and self contained

## Zoomies Zero Limitations

New experimental features are being added on top of Zero AOT that should bridge the gap between nnue and hce. 

Performance of Zero AOT currently matches Native AOT due to shared compiler but is expected to be faster once fully developed




