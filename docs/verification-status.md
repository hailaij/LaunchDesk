# Verification Status And Evidence Index

Historical pre-v0.7.0 snapshot, recorded 2026-10-12. Base HEAD was
`43449c5a876096dd0af75fcce9fa482e78d342f9` with uncommitted changes.
Use the recorded source hashes to identify those tests. The changes are included in
v0.7.0; this historical inventory is not its version-bumped release acceptance.
For the release commit, CI run and binary hashes, see the
[v0.7.0 release verification](https://github.com/hailaij/LaunchDesk/releases/tag/v0.7.0).

| Scope | Evidence | Verified | Not Verified |
| --- | --- | --- | --- |
| Remote baseline | Actions run 38215650657; `signing-drill/38215650657/` | 73/73 main tests, four real isolated signature cases, UI and cleanup | Local unpushed changes; production signing |
| Local incremental logs | `log-incremental/` | 76/76 main tests, normal/compact UI, performance budgets and five negative gate fixtures | Latest cloud CI; every machine's frame rate |
| Local Windows 10 compatibility | `win10-compat/` | 74/74 historical tests, icon glyph checks, UTF-8/GBK decoding and UI | Physical Windows 10 hardware |
| Data/manager reproduction | `data-lifecycle-repro/verified/` | Six observations recorded; D-1 not reproduced, data edge cases reproduced | Production repairs; full settings-dialog UI regression |

All local roots above are under `artifacts/verification/`. Counts are tied to their
specific scope/date, not interchangeable. The current regression inventory is 76.
The six reproduction observations are not six additional passing regression groups.

## Reports

- [Signing drill](signing-drill.md): remote source/runner identity, signature cases,
  strict failure contracts and the missing post-cleanup certificate inventory.
- [Log performance](log-performance.md): incremental design, budgets, allocation
  measurement scope, raw metrics, selection and repeated-line tests.
- [Windows 10 compatibility](windows10-compatibility.md): local implementation and
  evidence, explicitly without physical Windows 10 acceptance.
- [Data lifecycle reproduction](data-lifecycle-reproduction.md): isolated reproducers,
  observations and outstanding repairs; no user service/configuration was modified.
- [Trust and engineering](trust-and-engineering.md): historical local verification,
  cloud drill, production signing constraints and remaining work.
- [Test summary](test-summary.md): main-suite versus expected-negative contract reports.

## Integrity And Provenance

`artifacts/verification/evidence-index-2026-10-12.json` enumerates accepted reports,
screenshots, logs and environment/source snapshots from the four scopes above.
Each item records a project-relative path, byte length and SHA-256. Exploratory
failure runs and large reproduction sandboxes are deliberately not accepted evidence.
The index does not include itself and is a point-in-time snapshot; later changes
require regeneration and scope review, not silently updating historical results.

SHA-256 detects changes against this inventory. It is not a publisher signature,
remote attestation or proof that a test executed. CI run metadata/logs and test
reports provide execution provenance; local source hashes identify the dirty tree.
Raw files remain git-ignored. Documentation records the conclusions and public run
link but does not publish private keys, credentials, or a production certificate.

The inventory was read back and all 238 file lengths/hashes verified. The seven
runtime/test source hashes in the local log environment report still match the
current files. Documentation-only edits do not constitute another test execution.

Recorded SHA-256 anchors (not digital signatures):

```text
remote signing-drill main test-results.json
D576DBB3374870B268601473E4FDB798C0F935DCA18CDEDEBF4FECAE8C1F3AAB
local log-incremental main test-results.json
0CFFF0194CAD867815E1C80A507CC00E631ACC9323C13F08F6843001C6ED691C
evidence-index-2026-10-12.json
65449DEF89070106F2DE68402B62BCAE320C16A793AA12338B41CB08313C6C13
```

## Outstanding Acceptance

1. Authorize commit/push and run the current 76-group tree on cloud CI, including
   incremental performance gates. The 73-group remote drill cannot replace this.
2. Repeat representative icon/encoding checks on actual Windows 10 hardware.
3. Repair the reproduced data-saving/migration edge cases in a separately scoped task.
4. A future isolated drill should upload an explicit post-cleanup certificate/key
   inventory. Current logs prove successful removal commands, not that independent check.
5. Production signing remains unconfigured and fail-closed. No trusted signed installer,
   SignPath approval, silent installation or automatic installation rollback is claimed.
