Original prompt: Add a GitHub Actions build and GitHub Pages deployment of the Space Battle web version, and link the live demo from the Space Battle report.

## Work Log

- Selected a real .NET WebAssembly port of the shared Core + SECS simulation with a Canvas renderer, not a JavaScript approximation.
- Browser performance is separate from the recorded native benchmark results.
- Frontend and bilingual report links were implemented in independent file scopes.
- Build outputs and dependency caches stay inside the project and do not replace measured native binaries.

## Validation

- .NET 8 publish succeeds without installing external workloads; Core/SECS native benchmark binary hashes remain unchanged.
- Actual WASM combat validated through 600 ticks for low/nominal/high; same-browser reset and split-step checksums match.
- Browser counters confirm predictive spread, neutral friendly damage, self hits, docking and production.
- Fixed 320 px toolbar overflow found by the responsive test.
- Fixed hull/shield telemetry: presentation values are normalized fractions, not raw HP.
- Prescribed Playwright client completed two action bursts and screenshots were visually inspected.
- Documentation checker passed 28 documents / 600 local links; 23 Python tests passed.
- GitHub Pages configured for Actions and HTTPS at https://shortkedr.github.io/secs/.
- Final /secs/ build passed corrected telemetry, error recovery, touch selection and two-finger zoom, all profiles, desktop/mobile layout, and realtime controls.
- First Actions build/deploy succeeded: https://github.com/ShortKedr/secs/actions/runs/38137237606. Live HTML, .nojekyll and license inventory return HTTP 200.
- Removed an unsupported upload-pages-artifact v3 input (the action already archives dotfiles), and pinned Ubuntu 24.04 before its upcoming latest-label migration.

## Remaining

- Finish live-site browser checks and the final workflow run.
