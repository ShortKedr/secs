English | [Русский](VERSIONING.ru.md)

# SECS release versions

SECS uses a numeric [Semantic Versioning](https://semver.org/) version for
compatibility and a cedar-themed name for each release line. Show a release as:

`SECS vMAJOR.MINOR.PATCH — Name · YYYY-MM-DD`

The date is the actual publication date in ISO format. It is displayed in the
release title and notes, not in the package version or Git tag. The numeric
version is authoritative for dependency resolution; names and dates are for
people. Repository commits and benchmark reports continue to use exact Git
revisions for reproducibility.

| Planned line | Name | Example release title |
| --- | --- | --- |
| `0.1.x` | Cone | `SECS v0.1.0 — Cone · YYYY-MM-DD` |
| `0.2.x` | Branch | `SECS v0.2.0 — Branch · YYYY-MM-DD` |
| `1.0.x` | Canopy | `SECS v1.0.0 — Canopy · YYYY-MM-DD` |

These are naming examples, not published releases or promised dates. If more
`0.x` lines are needed before `1.0.0`, give each a distinct cedar-themed name;
reserve Canopy for the first stable public API. Patch releases keep their line's
name but use their own publication date, for example
`SECS v0.1.1 — Cone · YYYY-MM-DD`.

## Release artwork

These illustrations form a visual series based on the existing ShortKedr ECS
squirrel-and-cedar-cone emblem. They are ready for future release notes; they
do not announce a release. Add the actual version and publication date as text
when publishing, so those details remain readable and searchable.

| Cone | Branch | Canopy |
| --- | --- | --- |
| ![Squirrel curled around a luminous cedar cone](../branding/releases/cone.png) | ![Squirrel climbing a cedar branch](../branding/releases/branch.png) | ![Squirrel above a broad cedar canopy](../branding/releases/canopy.png) |

[Artwork sources and generation notes](../branding/releases/README.md).

Before `1.0.0`, the public API is still in development; release notes must call
out breaking changes. From `1.0.0` onward, increment MAJOR for incompatible
public API changes, MINOR for backward-compatible features, and PATCH for
backward-compatible fixes. Use standard prerelease suffixes such as `-rc.1`
when needed. A library release uses a Git tag such as `secs-v0.1.0` and package
version `0.1.0`; its title carries the name and date. Do not create a release
tag or assign a package version merely to announce this naming policy.
