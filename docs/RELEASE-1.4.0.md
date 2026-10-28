# Wavebreak 1.4.0 release record

Release: [v1.4.0](https://github.com/mPhpMaster/wavebreak-godot-game/releases/tag/v1.4.0).

Built with `build.ps1`: Godot 4.7.1 .NET Windows release export and Inno Setup 6. The package contains 191 exported files. Windows game version is 1.4.0.0; installer version is 1.4.0. The latest 1.3.4 longer-run and lobby-route changes were merged before building.

| Asset | SHA-256 |
| --- | --- |
| Wavebreak-Setup-1.4.0.exe | 7edd517bd1ad3922617166fb3d6894e2925e6faeca5d792f3953512d0b1cf706 |
| Wavebreak-1.4.0-windows.zip | 7084299d3d50104875ed0b82a4471ee164efaea64a3f238308c1318a90efa80b |

`SHA256SUMS.txt` accompanies both assets.

Validation completed before publication:

- Build with the two existing CS0108/CS8602 warnings; changed C# files remain within 500 lines.
- Full post-merge expedition regression suite, including both real ENet peers and host-accepted dash core damage.
- All tier/route phase combinations, retaining Normal/Hard four-map runs and higher-tier longer runs.
- 34 updater checks, including cancellation at helper acknowledgement, plus actual Windows PowerShell 5.1 temporary portable installation/rollback/cancel/timeout/integrity fixtures.
- Exported executable: city, reef, content audit and all 34 updater checks passed. Music checks validate actual imported streams; source size budgets remain source checks.
- English/Arabic update pages, notification, roof/giant/tether renders; actual exported title render shows 1.4.0.
- ZIP extracted using the actual exported updater assembly; every file hash matched the export directory, required layout/headers passed, and embedded helper resources matched current sources with UTF-8 BOM.
- Four balanced 90-second city source-engine runs and two 90-second actual Windows executable gameplay runs. Shipped-gameplay candidate P99 was 9.82 ms city and 11.95 ms reef, with zero steady frames above33 ms. Only audit and title cancellation guards changed afterward.

The earlier severe warm-play stall did not reproduce in balanced final measurements; its cause remains unconfirmed. Cold-load spikes remain. Manual listening/player feel, live Steam peer acceptance and running the installer over an actual installation remain unverified. No real installation was replaced and no user game session was stopped.

See [full verification](EXPEDITION-FIX-VERIFICATION.md), [performance data](EXPEDITION-SOAK-PERFORMANCE.json) and [updater behavior](UPDATER.md).
