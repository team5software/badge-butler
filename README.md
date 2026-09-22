# badge-butler

![release](https://badges.team5software.de/badges/badge-butler-release)
![build](https://badges.team5software.de/badges/badge-butler-build)
![tests](https://badges.team5software.de/badges/badge-butler-tests)

Stores and serves status badges (build, coverage, etc.) for repos — small, self-hosted, no
dependency on shields.io. Values are self-rendered as SVG from state kept in Postgres, served by
the deployment at [`badges.team5software.de`](https://badges.team5software.de) and updated
automatically by the workflows below.

`tests` currently reads "failing" — accurate, not a display bug: there's no test project yet, so
the CI test step has nothing to run and reports failure. `build` is unaffected (it reflects
compilation only). A `coverage` badge will show up here once real tests exist to produce one.

> **Source of truth: [Gitea](https://gitea.team5software.de/t5s/badge-butler).**
> [github.com/team5software/badge-butler](https://github.com/team5software/badge-butler) is a
> read-only push mirror, kept in sync automatically. Open issues/PRs on Gitea, not GitHub.

## API

- `GET /badges/{key}` — the badge SVG. Never requires auth.
- `PUT /badges/{key}` — upsert `{label, message, color}` (`color` optional, defaults to
  `lightgrey`). Auth-gated if `Auth:ApiKey` is configured.
- `DELETE /badges/{key}` — remove a badge. Same auth gating as `PUT`.
- `GET /health` — liveness/readiness probe target.

Auth is a single fixed key, set at deployment time (`Auth__ApiKey` env var). Unset it and writes
are open — fine for a private/LAN-only deployment. When set, write requests need
`X-Api-Key: <key>`.

## Running it

- `Dockerfile` — multi-stage, multi-arch (`linux/amd64`, `linux/arm64`) build.
- `charts/badge-butler/` — Helm chart; see its own [README](charts/badge-butler/README.md) for
  values and install instructions.

Images and the chart are published on tagged releases (`vX.Y.Z`) to both registries:

| | Image | Chart |
|---|---|---|
| Gitea | `gitea.team5software.de/t5s/badge-butler` | `oci://gitea.team5software.de/t5s/charts` |
| GHCR | `ghcr.io/team5software/badge-butler` | `oci://ghcr.io/team5software/charts` |

`develop` pushes also publish `latest`/`unstable` image tags (no chart) for anyone tracking
ongoing development rather than tagged releases.
