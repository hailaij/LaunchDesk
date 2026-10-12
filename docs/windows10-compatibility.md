# Windows 10 Compatibility Repair

Local verification on 2026-10-12, after the published v0.6.0 release:

- Replaced the Windows 11-only Segoe Fluent Icons dependency with one shared
  Segoe MDL2 Assets resource for buttons, row icons, checkmarks and dropdowns.
  Body text now uses Segoe UI with Microsoft YaHei UI fallback.
- UI checks verify all 22 icon code points resolve to nonzero glyph indices.
  Chinese normal-size and English minimum-size screenshots passed, along with
  virtualization, timer recovery and fatal process-tree cleanup checks.
- New items default to auto output decoding. Each pipe independently validates
  UTF-8, buffers incomplete characters, and falls back to GetACP() on invalid
  bytes. Explicit system, UTF-8, GBK and other encodings remain available.
  Existing settings, historical log text and captured raw bytes are preserved.
- 74 core test groups passed, none failed or skipped. Local signature tests used
  the existing trusted Inno compiler public fixture, not a production certificate.
  Engineering contracts also passed; deliberately failing/skipped negative
  reports are separate from the full-suite result.
- Evidence: artifacts/verification/win10-compat/test-results.json and ui/.
  These are local results, not cloud CI acceptance or physical Windows 10 testing.

For an existing affected item, change its encoding from utf-8 to auto or gbk and
restart it. This only affects new output. Automatic detection is heuristic;
mixed encodings or legacy bytes that are also valid UTF-8 need explicit settings.
Auto stdin remains UTF-8. Do not change the system-wide UTF-8 locale setting.

The reported Steam++ Unregister-ScheduledTask ObjectNotFound error is an external
application error, independent of decoding. A GUI application without a console
may reject Ctrl+C; managed-tree termination after the grace timeout is unchanged.

The preview installer is unsigned, retains version 0.6.0, and is kept separately
under artifacts/previews/win10-compat. It does not replace the published release.
