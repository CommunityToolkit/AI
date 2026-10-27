---
name: publish-providers
description: Publish one or more MEVD providers when the user explicitly requests a release, for example "publish Chroma and PgVector". Never publish when merely discussing or editing this skill.
---

1. Require explicitly named providers; never infer additional releases. Verify `origin` points to `CommunityToolkit/AI`.
2. Fetch `origin main --tags` and pin the release commit to `origin/main`. Do not switch branches, commit changes, or bump versions.
3. At that commit, resolve each provider to its exact directory name under `MEVD/src`. Read its literal `<Version>` from its `.csproj`; form `mevd/<Provider>/v<Version>`. Stop on unknown providers or ambiguous versions.
4. Preflight every requested tag locally and remotely before making changes. Stop on any tag pointing to another commit; skip remote tags already targeting the release commit. Reuse matching local tags.
5. For each remaining tag, create it at the pinned commit if missing, then run `git push origin refs/tags/<tag>`. Push tags individually so each triggers GitHub Actions. Never force-push, push branches, or use `--tags`. Stop on failure and report any tags already pushed.
6. Verify the remote tag targets and report the provider/version, tag, and workflow link. Distinguish a triggered release from a published package: NuGet publishing still requires approval of `nuget-release-gate`.
