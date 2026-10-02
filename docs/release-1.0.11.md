# 1.0.11 — application update checks

Settings → General now shows the installed version and a manual update check.
It displays the latest stable GitHub release and its notes, and opens the MSI
download or release page in the default browser. The application never runs an
installer or restarts itself. Finish recordings and close the app before installing.

Optional startup checks are disabled by default. Enable them in Settings and
save to receive a nonmodal update notice in the main window. Offline startup
remains quiet; manual failures show a retry message. Concurrent checks share a
request with a 15-second timeout and a bounded response size. No GitHub account,
meeting content or credentials are used. Release text is plain text; download
links are restricted to the project's repository and expected Windows MSI.

The release remains unsigned while the SignPath application is pending.

## Verification

All 49 Core regression checks pass, including numeric version comparison,
untrusted link rejection, draft/prerelease filtering, HTTP failures, response
limits and cancellation. The Release solution builds without warnings. A live
GitHub API check detected the published 1.0.10 installer. The Settings view was
rendered offscreen with a simulated newer release and visually inspected.
