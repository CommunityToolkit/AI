# NuGet Publishing

Packages are published via GitHub Actions triggered by git tags. Each component workflow (e.g. `mevd.yml` or `document-extraction.yml`) owns its build, test, and pack steps; on tag pushes it calls the generic `release.yml` reusable workflow to sign and publish to NuGet.org.

## Tag format

- `<component>/v<version>` — release all packages in a component (e.g. `mevd/v1.0.0-preview.1`)
- `<component>/<subcomponent>/v<version>` — release a single package (e.g. `mevd/PgVector/v1.0.0-preview.1`)

DocumentExtraction uses the `document-extraction` tag prefix (e.g. `document-extraction/PdfPig/v1.0.0-preview`).

The subcomponent name must match the directory name under the component's `src/` folder exactly (e.g. `PgVector`, `Qdrant`, `InMemory`).

Preview/prerelease versions use standard NuGet prerelease suffixes in the tag (e.g. `v1.0.0-preview.1`).

## Updating MEVD providers

When changing C# files under `MEVD/src/<provider>/`, also update the `<Version>` in the provider's `.csproj`. Choose the appropriate SemVer increment for the change. Changes limited to tests, documentation, shared code, or central build and dependency files do not require a provider version update.

Release tags must exactly match project versions. A single-provider tag must match that provider's version, while a component-wide `<component>/v<version>` tag requires every provider in that component to have that version.

## Release flow

Pushing a matching tag triggers the component workflow, which runs the full test suite, packs NuGet packages, then hands them to `release.yml` for signing (Azure Key Vault) → GitHub Release → NuGet.org (gated by manual approval on the `nuget-release-gate` environment).

## Adding a new component

Create a `<component>.yml` workflow that:
1. Triggers on its own tag prefix (e.g. `mycomp/v*`)
2. Handles all component-specific build and test logic
3. On tag pushes, packs NuGet packages and calls `release.yml`
