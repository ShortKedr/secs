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

- The previous interpreter build is still live. The current user instructions prohibit remote changes; do not push or dispatch Actions without renewed authorization.

## Browser Performance Correction

- User clarified that the browser version must run like a real game simulation, not export JSON snapshots from interpreted C# every frame.
- Old live build measured locally in Chromium at nominal seed 7331 after 600 ticks: mean Core Step 15.01 ms, frame export 29.28 ms, JSON parse 0.44 ms. These are browser-port diagnostics, not replacements for the native ECS benchmark results.
- Production publish now enables WebAssembly AOT and native relinking; CI installs the matching SDK workload and checks the runtime mode before deployment.
- Replaced reflection/JSON invocation with direct JS exports and reusable typed views into a native WASM render buffer. Full checksums are debug/test-only; simulation rules and workload are unchanged.
- Interpreter-mode binary bridge passed real Chromium tests for all profiles, deterministic resets, combat, responsive controls, touch and error recovery.
- Local Release AOT publish succeeded with 47 compiled assemblies, including shared Core, SECS and Aether.Physics2D. SDK 8.0.425 and wasm-tools 8.0.31 are installed only under `Benchmarks.Local/tmp/dotnet-aot`; system SDKs were not changed.
- AOT build passed the complete real-browser checker and the prescribed Playwright action client. Desktop/mobile screenshots were visually inspected; no console/page errors. All three 600-tick checksums match the interpreter binary bridge in the same Chromium.
- Repeated profiles sequentially in Chromium 151, 1440x900, nominal seed 7331, 600 warmup ticks: Core Step mean 15.90 -> 7.40 ms; render export 29.76 -> 3.04 ms; realtime 8.38 -> 58.02 FPS and 49.13 -> 57.82 simulation ticks/s. Earlier AOT run reached 59.84 FPS; this is not a universal FPS guarantee.
- A denser early-phase AOT sample (240 warmup ticks) measured 56.60 FPS / 56.40 simulation ticks/s, with mean Core Step 13.98 ms and render export 4.68 ms. Browser simulation still has real CPU cost; no entities or physics were removed to meet a frame target.
- Profiler evidence: `Benchmarks.Local/tmp/web-performance-before-verified.json`, `web-performance-aot.json`, `web-performance-aot-peak.json`. Browser evidence: `Benchmarks.Local/tmp/web-aot-checks` and `web-aot-client`.
- Documentation checker now passes 28 documents / 606 links; 23 Python tests and the 181-source vendor verification passed. Native Core/SECS/Game benchmark binaries retain their original hashes.
- Added exact Emscripten 3.1.34 license and author list from the workload to browser notices. Compiler binaries and all SDK/cache/build outputs remain ignored local files.
- Local AOT static server is running at `http://127.0.0.1:8023/?lang=ru` (hidden Python process PID 48284). Restart with Python serving `Benchmarks.Local/tmp/web-aot/wwwroot` if needed.
- No push, Actions dispatch, or remote configuration changes were made during this correction. Publishing the updated AOT build requires renewed user authorization under the current remote-state restriction.

## Space Battle Report Heatmap

- Added nine-step green/yellow/red comparison markers and gray all-equal markers to EN/RU Markdown reports. Local static SVG assets preserve GitHub compatibility without inline CSS or external badge services; values remain selectable text.
- Timing, allocation and footprint comparisons are scoped by profile/metric; stage comparisons are scoped by row. Unknown/singleton metrics and neutral population, gameplay, GC, trend and frontend diagnostics are not ranked. Tied extremes share their color and bold treatment.
- The analyzer regenerates markers and both reports from the same validated data. Existing CSVs, JSON evidence and chart bytes remain unchanged.
- Full offline analysis passed the 15 timing-run and three composition-replay evidence gates; 33 Python tests passed. Documentation checks passed 28 documents / 1262 local links / 190 publishable targets.
- Local Markdown-it/Chromium preview verified 325 loaded markers per locale in light/dark themes, no browser errors, and exact table text equality against the previous report in local Git. Screenshots and evidence are in `Benchmarks.Local/tmp/report-heatmap-checks`.
- No push or other remote-state change was performed.

## Muted Browser Debris

- Changed browser debris from the bright shared presentation color to neutral gray (`#606060`). Ships, projectiles and short-lived combat particles keep their existing colors.
- Kept debris sizing in world space, including zoom, and left simulation data, collision radii and damage unchanged. Native renderer was inspected but is outside this browser-only change.
- AOT publish and complete browser checks passed. One actual debris row was pixel-checked at zoom 0.35/1/4/12/20; high zoom remains RGB 96/96/96, while antialiasing at low zoom stays muted. Simulation checksum is unchanged by the visual probe.
- The prescribed Playwright client completed two battle/action bursts; gameplay screenshots were visually inspected. Evidence is in `Benchmarks.Local/tmp/web-debris-checks` and `web-debris-client`. No console errors or remote changes.

## Fixed Heatmap Effect Scale

- User requested replacement of group min-max stretching with fixed 5/15/30 percent lag thresholds and explicit distinguishability safeguards.
- A read-only audit confirmed one comparable run per profile/framework; tick StddevMs, minute windows, smoke, frontend and composition replays cannot supply independent timing-repeat uncertainty.
- Preserve existing frozen evidence and all report table values/links. Add explicit independent-run summaries and scoped, documented absolute timing thresholds to the presentation helper; all default thresholds remain disabled because no calibrated practical floor is available.
- Replaced group min-max stretching with fixed, nine-step 5/15/30 percent effect colors. Entire practically close or uncertainty-overlapped groups are gray; no numeric extrema are bold. Comparison uses raw metrics while text precision and suffixes remain unchanged. Full red starts at 30%, not at a rounded palette boundary below it.
- Independent run summaries use per-metric medians and conservative observed min-max envelopes, never tick/window pseudo-replication or lucky singleton samples. Incomplete repeat coverage is neutral. Exact profile/metric timing thresholds require bilingual rationale; the report prints any chosen policy. Current defaults remain explicitly unavailable/disabled, not empirically calibrated zero-noise claims.
- EN/RU legends and methodology document formulas, zeros, agreed effect thresholds, missing independent repeats, absolute-policy units/scope and rationale. Neutral gameplay/composition/GC/trend diagnostics remain unranked.
- Offline analyzer passed all original evidence gates twice. 41 Space Battle tests plus 14 general tool tests passed (55 total); docs passed 28 bilingual documents / 1264 local links / 191 publishable targets.
- Markdown-it/Chromium checks confirmed exact pre-change table cell text and all non-indicator links in both reports, zero bold numeric extremes, all SVGs loaded, and no browser errors in light/dark themes. Latest screenshots were visually inspected in `Benchmarks.Local/tmp/report-fixed-scale-checks`.
- Final hashes confirm all 77 protected JSON/CSV/PNG files are unchanged. No new benchmark, commit, push or other remote mutation occurred; unrelated local edits are preserved.

## Tactical AI Revision

- User requested stable capability-based target locks, small shared-focus squads, and wounded members retreating behind healthier allies.
- Added deterministic pre-AI snapshots and tactical-lock-v2 rules in shared Core for all five native ECS ports. Persistent target/squad/cover state stays in Brain; CombatState records live enemy projectile attackers. Component type counts are unchanged.
- Target decisions use expected weapon output, heat, shield/armor health, intercept viability and approach cost. A 120-tick lease and 35-percent improvement gate reject nearest-distance oscillation; invalid targets release immediately.
- Stable 2-4 member Interceptor/Fighter/Bomber/Gunship squads share focus; threatened wounded members screen behind healthier allies without firing. Critical damage/heat preserves carrier repair retreat. Carrier orbit/turn speed is reduced with bounded angular acceleration.
- Isolated Release build, complete Core/tactical validation and final 3000-tick five-port parity passed: all ten checkpoints match, final checksum 010CC89FADD52A65. Frozen native benchmark binaries were not rebuilt. NuGet vulnerability-audit lookup was unavailable; cached package restore/build succeeded.
- Added edge regressions for in-range outward-moving carrier targets, unreachable escaping targets, stale enemy attribution after friendly damage, cover-chain avoidance, dead-cover repair and settling at the rear cover point.
- Browser testing exposed Mono WASM AOT calling-convention failure with a large snapshot value type nested inside generic Battle<TStore>. Moving the immutable snapshot to a nongeneric type fixed the real AOT runtime failure; no interpreter fallback was introduced.
- Five-port replay caught order-dependent cover-velocity reads during steering. Cover now follows the immutable pre-tick ally velocity, preserving adapter-independent updates. The shared formation scheduler is included in gameplay checksum state.
- Final Release builds of Headless and native Game and production AOT publish succeeded using isolated project-local artifacts. Browser checker passed all three profiles, same-browser reset/split-step parity, live squad bounds/shared focus, cover orders, neutral projectile damage, controls, desktop/mobile/touch/error recovery and muted debris at five zooms. No console/page errors.
- Final nominal browser validation at tick 600 observed 253 group orders, 1829 target retentions and 46 cover orders. These are scenario diagnostics, not evidence of a speed or tactical-superiority ranking. Evidence is in Benchmarks.Local/tmp/web-tactics-final-checks.
- Prescribed Playwright client completed two 240-tick battle bursts; desktop/mobile/client screenshots were visually inspected. Evidence is in Benchmarks.Local/tmp/web-tactics-client. Local AOT demo remains available at http://127.0.0.1:8023/?lang=ru.
- Bilingual tactical documentation and historical-report notices are complete. Offline rendering passed the original frozen evidence gate, 42 Space Battle Python tests plus 14 general tool tests passed, and docs passed 30 bilingual documents / 1286 local links / 194 publishable targets. All original report table text and original URLs are preserved; all 77 JSON/CSV/PNG evidence hashes and original native benchmark DLL hashes remain unchanged.
- Old report evidence remains predictive-neutral-v1, not new-AI performance. No new timing benchmark, commit, push, Actions dispatch or other remote mutation occurred. Unrelated local changes were preserved.

## Live Frontend Statistics

- User requested simulation performance/entity statistics in both the WebAssembly and OpenGL frontends.
- Added a shared allocation-free 600-tick rolling statistics buffer outside Core Step, nearest-rank p95/p99, maximum, mean and nine stage means. Tick samples are not independent benchmark repeats.
- Web binary header v2 exports bounded timing/storage/physics counters without per-frame JSON. A chart button opens a bilingual, scrollable diagnostics panel; the footer retains a compact live summary.
- Desktop HUD adds alive/docked/offscreen ECS category totals, wall ticks/s separate from FPS and render submission CPU, plus an F3/clickable chart panel and storage/physics summary.
- Real rates exclude offline fast-forward; pause clears throughput, reset clears tick statistics. Requested budget follows simulation speed and excludes presentation costs. No source benchmark evidence is replaced.
- Headless statistics tests passed: zero/single/rolling/invalid/reset samples, stage means, quantiles, no sampling allocations, true ECS counts and checksum neutrality. Isolated Headless and Game Release builds succeeded.
- Final hidden OpenGL captures passed rendering/control/statistics invariants at 960x540, 1440x900 and actual 3840x1421 (OS clamped the requested 3840x2160). Minimum/desktop screenshots were visually inspected. Real Update/Render loop validation passed positive tick/FPS rates, paused render cadence/history, speed budget and reset.
- Production AOT publish and complete Chromium checker passed all profiles, checksum/reset parity, squad/cover combat, live rates, EN/RU statistics, mobile/landscape layout, touch/error recovery and muted debris. Fixed a two-pixel landscape camera-tool overlap found by the new layout assertions.
- Prescribed Playwright action client completed two 240-tick bursts; latest scene and full statistics screenshots were inspected. No console/page errors. Evidence: Benchmarks.Local/tmp/statistics-native, web-statistics-final-checks and web-statistics-client.
- 42 Space Battle Python tests, 14 general tool tests and bilingual docs validation (30 documents / 1286 local links / 194 targets) passed. Existing reports/data and frozen Core/SECS/Game benchmark DLLs remain unchanged. No commit/push/remote changes for this task.
- Local AOT demo is served at http://127.0.0.1:8024/?lang=ru by hidden Python process 43436; the earlier port 8023 was left alone. No remaining feature TODOs.
