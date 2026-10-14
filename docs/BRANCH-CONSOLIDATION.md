# Branch consolidation, 14 October 2026

Ten obsolete branches were compared against main at `7190abf325ddc2ef7802a80cd500dd2791c5dbd8`. There were no open pull requests. Nine branches contain only patch-equivalent changes already on main. The older colour/transparency/font branch matches the aggregate patch in main commit `365d0d20af417c134476ebbf72a82ded42c1fad0`. No old source needs to replace the newer implementation.

The consolidation commit retains main as its first parent and the ten audited heads as additional parents, preserving their complete histories without replaying old code. This also makes ancestry a direct, machine-checkable prerequisite for cleanup.

| Branch | Audited head | Content evidence |
| --- | --- | --- |
| `codex/validate-cat-icon-v0.10.0-alpha.1` | `0510dcf10186c8dd27ae8e07c3932508e382fb01` | All branch-only patches already on main |
| `codex/validate-photo-frame-v0.10.0-alpha.1` | `c79996d1c2a3fd3d05351d8fd9bcb360fbcb00d5` | All branch-only patches already on main |
| `fix/color-persistence-transparency-font-size` | `774466a11801595915ef8cfaee7813a53ce35fbb` | Aggregate patch matches `365d0d2` |
| `release/0.9.0-alpha.1` | `465dbe2d5fc39ae7c2a4ece50b1b284ceeb099be` | All branch-only patches already on main |
| `release/verify-packaged-builds` | `5eb0fd4de473d46251a9a6c67bf036d186ef0ebc` | All branch-only patches already on main |
| `validate-0.9.1-alpha.1` | `c45a64b819c0afb192e49a2dcf1665aecc1cb4fb` | All branch-only patches already on main |
| `validate-0.9.1-alpha.2` | `4c7452b1f99b6c90b3ca3c524a4ecd90e732de98` | All branch-only patches already on main |
| `validate-0.9.1-alpha.3` | `5d6064aa71ace19322d8459fb6e96d93bd184be1` | All branch-only patches already on main |
| `validate/caption-workspace-0.10.0-alpha.3` | `3981011ec542cf4c7ce9afaff1e09f701758c76c` | All branch-only patches already on main |
| `validate/mode-workspaces-0.10.0-alpha.2` | `2e505c5473414c0208477ddc2668769f8dc641a4` | All branch-only patches already on main |

Cleanup runs once on the alpha.11 release push, after all Windows build, WPF, publishing and package checks succeed. It reads the fixed audit manifest, verifies the exact current branch head, checks that its history is an ancestor of the tested source, skips protected branches and branches with open pull requests, and checks the ref again immediately before deleting it. If a branch moved, it is kept. The consolidation branch is removable only when its current head is also an ancestor of the tested source. Release tags and historical releases are retained.

The alpha.11 source was recovered from `source-snapshot.zip` inside the delivered test package, whose build metadata identifies source `0c76976d712f09e96afdb6a93e1946326a2833b2`. Documentation and the guarded cleanup were added during consolidation.
