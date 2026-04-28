# NuGet Publishing

Packages are published via GitHub Actions triggered by git tags. Each component workflow (e.g. `mevd.yml`) owns its build, test, and pack steps; on tag pushes it calls the generic `sign-and-publish.yml` reusable workflow to sign and publish to NuGet.org.

## Tag format

- `<component>/v<version>` — release all packages in a component (e.g. `mevd/v1.0.0-preview.1`)
- `<component>/<subcomponent>/v<version>` — release a single package (e.g. `mevd/PgVector/v1.0.0-preview.1`)

The subcomponent name must match the directory name under the component's `src/` folder exactly (e.g. `PgVector`, `Qdrant`, `InMemory`).

Preview/prerelease versions use standard NuGet prerelease suffixes in the tag (e.g. `v1.0.0-preview.1`).

## Release flow

Pushing a matching tag triggers the component workflow, which runs the full test suite, packs NuGet packages, then hands them to `sign-and-publish.yml` for signing (Azure Key Vault) → GitHub Release → NuGet.org (gated by manual approval on the `nuget-release-gate` environment).

## Adding a new component

Create a `<component>.yml` workflow that:
1. Triggers on its own tag prefix (e.g. `mycomp/v*`)
2. Handles all component-specific build and test logic
3. On tag pushes, calls `package.yml` then `sign-and-publish.yml`
