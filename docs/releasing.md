# Releasing

Releases of the s&box Analytics SDK are automated with
[release-please](https://github.com/googleapis/release-please) and GitHub Actions.

There is nothing to build or publish here beyond the tagged Git source — the
pipeline only versions the library. Consumers pull it by tag.

## Flow

1. Merge work into `main` using [Conventional Commits](https://www.conventionalcommits.org/)
   (`feat:`, `fix:`, `feat!:` / `BREAKING CHANGE:` drive the semver bump).
2. `release.yml` maintains a rolling **release PR** that accumulates the changelog
   and version bump (`version.txt` + `.release-please-manifest.json`).
3. Merging the release PR creates the `vX.Y.Z` tag and a **GitHub Release** with
   source archives attached automatically.

No manual tagging, no manual changelog edits — everything is derived from commit
messages.

## One-time repository setup

- Create the `main` branch (e.g. `git push origin dev:main`) — release automation
  only runs there.
- In repo **Settings → Actions → General**, enable **"Allow GitHub Actions to
  create and approve pull requests"** (release-please opens the release PR with
  `GITHUB_TOKEN`).

## Manual / coordinated releases

`release.yml` also accepts `workflow_dispatch`, so a maintainer (or the platform
repo, when an ingest contract change requires lockstep SDK + platform releases)
can trigger a release run:

```sh
gh workflow run release.yml --repo Noot-Studio/analytics-library --ref main
```

When an ingest contract change spans both sides, land and release the platform
change first, then release the SDK against the published version.
