# Agent guidance

This workspace keeps the independent private repository at `./skecs-private/`.
Before writing plans, working notes, or documentation for the maintainer and
agents, read `skecs-private/AGENTS.md` ([private agent instructions](https://github.com/ShortKedr/skecs-private/blob/main/AGENTS.md)).
The private checkout is always a directory at the root of this workspace and is ignored by
the public repository; it is not a submodule.

Keep repository operations in sync: when committing changes in the public repository,
also commit the related changes in `skecs-private/`. When pushing the public repository,
also push `skecs-private/` without a separate reminder. The workspace's prohibition on
changing remote state still applies: do not push either repository while that prohibition
is in force.

Private repository: [ShortKedr/skecs-private](https://github.com/ShortKedr/skecs-private)
(restricted access).
