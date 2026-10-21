# Push releases and Pages

The build workflow runs on every branch push, pull request and manual dispatch. Pull requests validate without publishing. Successful branch pushes publish prereleases; successful default-branch pushes publish releases. Publication waits for web, Windows and Android builds, then attaches commit-based notes, SHA256 checksums, the universal APK, Windows EXE installer, portable ZIP and static web ZIP.

## One-time GitHub configuration

In **Settings → Pages → Build and deployment → Source**, select **GitHub Actions**. Only the default branch deploys to Pages. Relative Vite asset URLs support repository subpaths. User documents remain in the browser's IndexedDB; hosting does not introduce cloud file storage.

Allow Actions in repository settings. Release publishing requests `contents: write` only in its own job. Pages requests `pages: write` and `id-token: write`. Repository/organization policy may restrict these permissions. No personal access token is required by this workflow.

## Android signing

Add Actions secrets `ANDROID_KEYSTORE_BASE64` (base64 of your JKS), `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, and `ANDROID_KEY_PASSWORD`. These keep the same signing identity across builds. Keep the key backed up outside the repository.

Without these secrets, release builds use the runner's ephemeral Android debug key. Workflow warnings and release notes identify the fallback. Differently signed APKs cannot update each other: uninstall an older preview first. Git ignores key.properties, JKS and keystore files.

## Windows packaging

The workflow builds/tests the C++ engine, publishes the WinUI app with its .NET and Windows App SDK dependencies, and packages it using Inno Setup. The EXE installs per-user; the portable ZIP contains the same published application. This supplies the requested EXE option; MSIX certificate infrastructure is not configured. The EXE is not Authenticode signed.

BuildTools `10.0.26100.4654` matches the pinned Windows App SDK's minimum. Do not restore the older `10.0.26100.3916` pin or suppress NU1605.

Local checks cannot run GitHub-hosted publication or configure repository Pages settings. Check the first Actions run after pushing. Artifact upload fails when an expected binary is absent.
