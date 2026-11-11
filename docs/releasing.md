# Releasing

Publication is manual, not triggered by an ordinary push or pull request.

1. Update the library project's `Version`, sample's pinned `PackageReference`, README installation version and actual changelog changes together. Versions are selected by the maintainer, not automatically incremented.
2. Build, test and pack in Release as described in CONTRIBUTING.md. Inspect package metadata, README/icon/licence, XML documentation, framework references and source/symbol information. Run the sample against that packed version and review public contents.
3. Push the reviewed main-branch commit. In Actions, select **Publish NuGet**, branch **main**, and mode **authenticate** to validate Trusted Publishing without a package push.
4. Select mode **publish** only after release checks and publication authorization. The workflow reads the ID/version from the project, validates the solution and packed sample, then uploads the validated package and associated symbols.
5. Verify accepted upload separately from NuGet listing and public download availability. Restore the released version into a fresh package cache before confirming a consumer migration. Never republish a version just because indexing is delayed.
6. Create a matching GitHub release/tag at the package's source commit with the validated artifacts. Tighten a first-publication policy to permit only new versions afterwards.

Trusted Publishing uses NuGet username `BrettWhittaker`, GitHub repository `bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging`, workflow `publish-nuget.yml`, environment `nuget`, and the exact package ID. The environment permits main-branch deployments only. Authentication and publishing jobs alone receive `id-token: write`; no permanent API key is stored.

An authentication-only run cannot publish. Its successful exchange is evidence of authentication, not independent confirmation of permanent policy activation in the NuGet account UI. An unused initial policy can expire after seven days and require reactivation. Credentials must never be copied into logs, artifacts or release notes.
