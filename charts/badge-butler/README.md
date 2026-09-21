# badge-butler

Stores and serves status badges (build, coverage, etc.) for repos.

## Prerequisites

A reachable Postgres database. This chart does not provision one — point `database.*` at an
existing instance (e.g. a [CloudNativePG](https://cloudnative-pg.io/) `Cluster`, or any managed
Postgres).

## Installing

```console
helm install badge-butler ./charts/badge-butler \
  --set database.host=my-postgres.example.svc.cluster.local \
  --set database.existingSecret.name=badge-butler-db-app
```

The referenced secret must have a `user` and `password` key (matching CloudNativePG's
auto-generated `<cluster>-app` secret by default — override the key names via
`database.existingSecret.userKey`/`passwordKey` otherwise).

For local testing without a pre-existing secret, set `database.user`/`database.password`
directly instead of `database.existingSecret.name` — the chart then renders its own Secret.
Never do this in a values file committed to a GitOps repo.

## Auth

Write endpoints (`PUT`/`DELETE /badges/{key}`) can require a fixed API key via
`X-Api-Key`; `GET` is always open. Leave `auth.existingSecret.name` and `auth.apiKey` both unset
to run with no auth at all — fine for a private/LAN-only deployment. Otherwise, same pattern as
`database`: `auth.existingSecret.name` (+ optional `.key`, default `apiKey`) for a pre-provisioned
Secret, or `auth.apiKey` for the chart to render its own (local testing only).

## Values

| Key | Default | Description |
|---|---|---|
| `image.repository` | `gitea.team5software.de/t5s/badge-butler` | Container image |
| `image.tag` | chart `appVersion` | Image tag |
| `database.host` / `.port` / `.name` | `""` / `5432` / `badgebutler` | Postgres connection target |
| `database.existingSecret.name` | `""` | Secret with `user`/`password` keys |
| `auth.existingSecret.name` | `""` | Secret with the API key (write endpoints); unset = no auth |
| `ingress.enabled` | `false` | Plain `networking.k8s.io/v1` Ingress |
| `service.port` | `80` | Service port |
| `resources` | `50m`/`128Mi` req, `256Mi` mem limit | Container resources |
| `autoscaling.enabled` | `false` | HPA on CPU |

See [`values.yaml`](values.yaml) for the full set, including `extraEnv`/`extraVolumes` escape
hatches.
