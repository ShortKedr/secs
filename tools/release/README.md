# SECS 0.1.0 release checks

The source of truth is [the release preparation specification](../../docs/secs/RELEASE_PREPARATION.ru.md). `secs-ci.yml` runs the core package and consumer checks on GitHub-hosted runners for PRs and `main`. Unity Editor and Windows Player checks run locally in fresh, isolated copies of the fixture. No permanent Unity runner is needed.

## Build an exact local candidate

Use a clean checkout of the intended release commit. Run these commands from its root in PowerShell 7; keep unrelated work in the original checkout. Do not substitute a package from an earlier commit, even for a documentation-only change: the package and Unity ZIP embed source metadata and the ZIP embeds the current installation guide.

```powershell
$commit = (git rev-parse HEAD).Trim()
$version = '0.1.0'
if (git status --porcelain --untracked-files=all) { throw 'Release checkout is not clean.' }
dotnet test SECS.Tests/SECS.Tests.csproj -c Release
dotnet build SECS/SECS.csproj -c Release
dotnet pack SECS/SECS.csproj -c Release --no-build -o artifacts/release -p:RepositoryCommit=$commit
python tools/release/verify_package.py "artifacts/release/ShortKedr.SECS.$version.nupkg" --version $version --commit $commit
python tools/release/make_unity_zip.py --package "artifacts/release/ShortKedr.SECS.$version.nupkg" --output "artifacts/release/SECS.Unity.$version.zip" --version $version --commit $commit
python tools/release/verify_unity_zip.py --package "artifacts/release/ShortKedr.SECS.$version.nupkg" --unity-zip "artifacts/release/SECS.Unity.$version.zip" --version $version --commit $commit
python tools/check_docs.py
```

Check each command's exit code. The package verifier checks the version, TFMs, dependencies, license, README and repository commit. The Unity ZIP verifier checks its DLL byte-for-byte against the package and checks its guide and manifest against the checkout.

## Run consumer smokes

Run the package-backed .NET 8, .NET 10, .NET Framework 4.8 and Godot 4.5 .NET fixtures in `tools/release-consumers/`. For Unity 2022.3 LTS and Unity 6, run each of `Editor`, `Mono` and `IL2CPP` with `Invoke-UnitySmoke.ps1` against the candidate ZIP. Each call creates a new test project under ignored `artifacts/release-consumers/`; the player cases build and run a Windows executable. Keep stdout/stderr and the Editor/player logs for every case. A build alone is not a passed player smoke.

Record the exact SDK, Godot and Unity Editor patch versions, OS, backend, full source SHA, log paths and SHA-256 of the `.nupkg`, Unity ZIP and `cone.png` in `artifacts/release/verification-0.1.0.json`. Mark a case `passed` only when its command and required runtime marker succeeded. Give every case an `exitCode: 0` and an `artifacts/` log with the marker required by `verify_local_report.py`. Player cases also need the Editor build log in `buildLog`. Run `python -B tools/release/verify_local_report.py artifacts/release/verification-0.1.0.json` before declaring `releaseReady: true`; any missing log, mismatched hash, dirty checkout or incomplete case keeps it false. The JSON is a local release artifact, not a source file to commit.

## Publish only after local acceptance

The first release is manual. When the hosted `SECS hosted checks` job and the exact-commit local report are green, create `secs-v0.1.0` at that commit. Prepare `SHA256SUMS.txt` and notes titled `SECS v0.1.0 — Cone · YYYY-MM-DD` using the actual publication date in Asia/Qyzylorda. Attach the package, Unity ZIP, `cone.png`, checksums and verification JSON to GitHub Release. Download the published files and compare them with the local SHA-256 values, then install the downloaded package in a fresh consumer. Do not overwrite an existing tag or release.

The old automatic tag workflow draft requires self-hosted Unity runners and does not use the local JSON. It is intentionally inactive; do not enable it for this release.
