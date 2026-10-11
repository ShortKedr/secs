English | [Русский](THIRD_PARTY_NOTICES.ru.md)

# Third-Party Sources and Licenses

[Repository](README.md) | [All implementations and tests](Benchmarks.Local/README.md) | [Benchmark report](docs/space-battle/REPORT.md)

This repository compares five ECS implementations using shared workloads. The
vendored code remains the work of its respective authors. Comparison, research,
or naming a framework does not waive its license conditions or imply endorsement.
This inventory records the checked source licenses, not a guarantee of compliance
with every law, jurisdiction, patent, or a user's individual circumstances.

## Pinned Source Inventory

Only the runtime source subsets below and their exact original licenses are
distributed. They are ordinary repository files, not submodules, external links
standing in for code, or a required download. All included upstream files are
byte-identical to the pinned archives; copyright headers are preserved.

| Component | Authors / copyright in license | Terms | Included source | Exact license |
| --- | --- | --- | --- | --- |
| LeoECS Classic | 2012 - 2023 leopotam@yandex.ru | MIT-Red or separately negotiated proprietary/commercial license | [ecs/src](Benchmarks.Local/vendor/ecs/src/) | [Original](Benchmarks.Local/vendor/ecs/LICENSE.md), [pinned upstream](https://github.com/Leopotam/ecs/blob/1213c31c9ef8325d48e74a52b345c4db242d038b/LICENSE.md) |
| LeoECS Lite | 2012-2024 leopotam@yandex.ru | MIT-Red | [ecslite/src](Benchmarks.Local/vendor/ecslite/src/) | [Original](Benchmarks.Local/vendor/ecslite/LICENSE.md), [pinned upstream](https://github.com/Leopotam/ecslite/blob/5053141d7d663c6b1855c8409ac93dfac807b239/LICENSE.md) |
| Morpeh | 2025 Oleg Morozov, Igor Boyko, Matvey Reschikov | MIT | [Morpeh Core](Benchmarks.Local/vendor/morpeh/Scellecs.Morpeh/Core/) | [Original](Benchmarks.Local/vendor/morpeh/LICENSE.md), [pinned upstream](https://github.com/scellecs/morpeh/blob/9f6369384792a01d74df24efd35b08ae313e4f12/LICENSE.md) |
| StaticEcs | 2024 FFS | MIT | [StaticEcs/Src](Benchmarks.Local/vendor/StaticEcs/Src/) | [Original](Benchmarks.Local/vendor/StaticEcs/LICENSE.md), [pinned upstream](https://github.com/Felid-Force-Studios/StaticEcs/blob/6323d710c22cfff70100fe3748de432cb5fc70c8/LICENSE.md) |
| StaticPack | 2025 FFS | MIT | [StaticPack/Src](Benchmarks.Local/vendor/StaticPack/Src/) | [Original](Benchmarks.Local/vendor/StaticPack/LICENSE.md), [pinned upstream](https://github.com/Felid-Force-Studios/StaticPack/blob/72ce8b993593e5cfd24c6a9289a4b132305ef493/LICENSE.md) |

StaticPack is a dependency of StaticEcs, not another measured ECS. Exact repository
URLs, full commit IDs, file sizes, and SHA-256 hashes are recorded in the
[source manifest](Benchmarks.Local/vendor-manifest.json).

## License Conditions

**MIT:** preserve each author's copyright and permission notice in all copies or
substantial portions, including any separately distributed binary/source package.
The original warranty/liability disclaimers remain in force. Do not remove or
replace these notices with a license for this repository's own code.

**MIT-Red is not ordinary MIT.** The pinned LeoECS licenses contain additional
conditions concerning support for military use of open-source software and
hostility or support for attacks toward Russians. They prohibit use when any
condition cannot be met, and require retaining the copyright and full conditions
in copies or substantial portions. The original Russian text is authoritative;
this short English description is not a replacement or a legal interpretation.
Users and redistributors must assess whether they meet those conditions. If they
cannot, they must not use the covered code under MIT-Red. Classic also describes
individually negotiated proprietary/commercial terms; no such permission is
claimed here. Do not label the complete comparison bundle unrestricted or
uniformly MIT-licensed. Obtain clarification from the rightsholder when needed.

## Local Adaptations

The upstream source files and licenses are unmodified. Our [microbenchmark
adapters](Benchmarks.Local/Adapters.cs), [static adapters](Benchmarks.Local/StaticAdapters.cs),
[stress adapters](Benchmarks.Local/StressAdapters.cs), and [SpaceBattle stores](SpaceBattle.Adapters/)
are separate comparison integrations, not upstream implementations or endorsed
performance recommendations.

[StaticLibrary.csproj](Benchmarks.Local/StaticLibrary.csproj) compiles StaticEcs and
StaticPack runtime sources for .NET 8. [MorpehLibrary.csproj](Benchmarks.Local/MorpehLibrary.csproj)
compiles the unchanged Morpeh Core for netstandard2.1, matching its upstream target
and Debug defines, without Unity/package/editor assets or generating a NuGet
package. These are local build wrappers, not edits to the ECS algorithms.
Packaging builds may have different binary hashes from historical measurements;
the retained benchmark provenance is not rewritten to claim otherwise.

## Other Dependencies

NuGet dependencies are declared in the project files and restored normally; their
package caches, binaries, SDKs, and native libraries are **not redistributed in
this source repository**. Examples include [Aether.Physics2D 2.2.0](https://www.nuget.org/packages/Aether.Physics2D/2.2.0)
(MS-PL), [Silk.NET 2.23.0](https://www.nuget.org/packages/Silk.NET.Windowing/2.23.0),
[StbImageSharp 2.30.16](https://www.nuget.org/packages/StbImageSharp/2.30.16),
[BenchmarkDotNet 0.14.0](https://www.nuget.org/packages/BenchmarkDotNet/0.14.0), and
the xUnit/test SDK/coverage tooling. This is not a complete transitive binary
license inventory. A future executable, NuGet package, or release archive needs
its own dependency/license review and required notices for everything bundled.
Generated frontend artwork has separate [provenance](SpaceBattle.Game/Assets/PROVENANCE.md).

No new license is assigned here to SECS, this repository's adapters, tests,
documentation, data, or artwork. Their publication license must be chosen by the
rightsholder; the absence of a root license does not mean public-domain or MIT.
Third-party terms above apply only to their covered code.

## Browser Distribution

The [WebAssembly demo](SpaceBattle.Web/README.md) distributes compiled SECS,
SpaceBattle.Core and SpaceBattle.SECS with Aether.Physics2D 2.2.0 (MS-PL),
.NET/Blazor 8.0.31 (MIT and upstream third-party notices), and Lucide 1.55.0
(ISC / MIT for Feather-derived icons). The static site ships the original license
texts, copyright notices and artwork provenance in its
[license directory](SpaceBattle.Web/wwwroot/licenses/), including a
[distribution inventory](SpaceBattle.Web/wwwroot/licenses/NOTICE.txt).
The demo does not bundle the four comparison ECS libraries, native OpenGL
dependencies, or benchmark/test dependencies. The broader source comparison
licensing conditions above remain unchanged.

## Verification

```powershell
python tools/check_vendor.py
python -m unittest discover -s tools -p 'test_check_vendor.py' -v
python tools/check_docs.py
```

The checker verifies hashes, license presence, exact path case, and that Git's
publishable source subset matches the manifest. [.gitattributes](.gitattributes)
prevents line-ending conversion of hash-verified upstream files.
[fetch.ps1](Benchmarks.Local/fetch.ps1) optionally restores missing manifest files
from pinned archives, checks their hashes before copying, and refuses to overwrite
changed local sources. A normal clone does not need this step.
