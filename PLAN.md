# Slint for Visual Studio 2026: port plan and status

A port of the Slint VS Code extension ([marketplace](https://marketplace.visualstudio.com/items?itemName=Slint.slint), source `editors/vscode` in [slint-ui/slint](https://github.com/slint-ui/slint)) to Visual Studio 2026 on Windows.

## Decisions

| Topic | Decision |
|---|---|
| Extension model | VisualStudio.Extensibility, out-of-process, .NET 8 (`net8.0-windows8.0`). No in-process VSSDK parts. |
| Live preview | Native window only, opened by `slint-lsp` itself. No WASM/WebView2 preview. |
| Repo | Standalone repo that bundles pinned `slint-lsp` release binaries (currently **1.18.1**). |
| Scope | Everything: core LSP, grammar, settings, restart, docs links, `slint!` in Rust, snippets, New Project, telemetry/panic reporting. |
| Licensing | Ask SixtyFPS whether this can be published under the official "Slint" publisher. Otherwise use a separate publisher with clear attribution and keep the upstream licence (`GPL-3.0-only OR LicenseRef-Slint-Royalty-free-2.0 OR LicenseRef-Slint-Software-3.0`). Placeholder publisher for now: `Slint Community`. |
| Rust files | Setting `attachToRust` (default **true**). When on, the server starts for a `.rs` file once it contains `slint!`. When off, `.rs` documents never reach the server. |
| Settings delivery | The proxy answers the server's `workspace/configuration` requests (section `slint`) itself, and changed settings are announced with `workspace/didChangeConfiguration`. Command-line flags (`-I`, `-L`, `--style`, `--no-toolbar`) aren't needed. |
| Out of scope | WASM preview (`wasm_preview.ts`), the browser/web extension, walkthroughs, Codespaces handling, status bar item, custom hover (replaced by a docs command). |

## Architecture

```
Visual Studio language client  <-- FullDuplexStream -->  LspProxy  <-- stdio -->  slint-lsp.exe
                                                           |
                       answers workspace/configuration (section "slint")
                       handles window/showDocument if VS doesn't advertise support
                       drops didOpen/didChange/didClose for .rs when attachToRust is off
                       captures telemetry/event; sends injected requests (ids "slint-vs-N")
                       records shutdown/exit so a later process exit isn't treated as a crash
```

- `slint-lsp` accepts `slint/showPreview` as a **notification** with params `[uri, component]`, so Show Preview doesn't go through VS's client.
- `slint-lsp` only asks for settings if `initialize` declares `capabilities.workspace.configuration = true`. The proxy adds that flag to the `initialize` request.
- Shape of the settings response: `{ includePaths: [], libraryPaths: { name: path }, preview: { style, hide_ui } }`.
- Server environment: `RUST_LOG=slint_lsp=<info|debug|trace>`, and `SLINT_LSP_PANIC_LOG_DIR=%LocalAppData%\Slint.VisualStudio\slint-lsp-panics`.
- Panic log format (`slint_lsp_panic_<pid>.log`): line 1 is the version, line 2 the location, line 3 the backtrace, and the remaining lines the message.

## Repo layout

```
Slint.VisualStudio.slnx
Directory.Build.props
src/Slint.VisualStudio.Core/          net8.0, platform-neutral, unit tested
  Lsp/LspMessageReader.cs             Content-Length framing (reader)
  Lsp/LspMessageWriter.cs             framing (writer, thread-safe)
  Lsp/LspProxy.cs                     message proxy (see Architecture)
  Lsp/LspTypes.cs                     ShowDocumentRequest, LspRequestException
  Server/LspBinaryLocator.cs          bundled / absolute / PATH lookup
  Server/ServerEnvironment.cs         RUST_LOG + panic-dir env, log level normalisation
  Server/SlintServerProcess.cs        process start, stderr capture, graceful kill
  Server/PanicLog.cs                  parse + take latest panic log
  SlintConfiguration.cs               settings record -> workspace/configuration JSON
  SlintDocuments.cs                   IsRust / WarrantsServer (slint! detection)
src/Slint.VisualStudio/               net8.0-windows8.0, VisualStudio.Extensibility
  SlintExtension.cs                   metadata, InstallationTargetVersion [18.0,), DI
  SlintSession.cs                     shared state: enable/restart, proxy, config, output pane, prompts
  SlintSettings.cs                    Tools > Options > Slint settings + read/subscribe
  SlintLanguageServerProvider.cs      document types, starts server + proxy, crash reporting, showDocument
  SlintDocumentListener.cs            text view listener that switches the provider on (lazy start)
  Commands/ShowPreviewCommand.cs      "Show Slint Preview"
  Commands/RestartServerCommand.cs    "Restart Slint Language Server"
  Commands/SlintMenu.cs               Extensions > Slint menu
  Slint.VisualStudio.csproj           FetchSlintLsp target (download + unzip x64/arm64 into bin/)
tests/Slint.VisualStudio.Core.Tests/  xUnit, net10.0 (36 tests)
```

## Settings (Tools > Options > Slint)

| Id | Type | Default | Notes |
|---|---|---|---|
| `lspBinaryPath` | string | `""` | Empty uses the bundled server; an absolute path is used as-is; a relative name is looked up on PATH. Takes effect after a restart. |
| `lspArgs` | string[] | `[]` | Extra arguments for `slint-lsp`. Takes effect after a restart. |
| `logLevel` | enum info/debug/trace | `info` | Takes effect after a restart. |
| `includePaths` | string[] | `[]` | Sent through `workspace/configuration`. |
| `libraryPaths` | string[] of `name=path` | `[]` | Name must match `^[a-zA-Z][a-zA-Z0-9-_]*$`. |
| `previewStyle` | string | `""` | fluent / material / cupertino / native |
| `previewHideUi` | bool | `false` | |
| `attachToRust` | bool | `true` | |

## Status

Last checked on macOS on 2026-10-30. `dotnet test` passes 36/36. `dotnet build -p:SkipSlintLspDownload=true` succeeds with 0 errors and 25 warnings, all CEE0027.

| Phase | Item | Status |
|---|---|---|
| 0 | Checks on Windows + VS 2026 | **TODO** (needs Windows) |
| 1 | Scaffold, manifest, bundled binaries | Done |
| 2 | Provider, proxy, binary locator, crash detection, lazy start | Done |
| 3 | Show Preview, Restart commands | Done (Extensions menu only) |
| 4 | TextMate grammar, snippets | **TODO** |
| 5 | Settings | Done |
| 6 | Docs command, New Project, Rust injection, telemetry | **TODO** (panic log parsing exists) |
| 7 | CI + Marketplace publishing | **TODO** |

### Build/test

```sh
dotnet test tests/Slint.VisualStudio.Core.Tests          # any OS
dotnet build Slint.VisualStudio.slnx                      # any OS; VSIX only produced on Windows
dotnet build ... -p:SkipSlintLspDownload=true             # skip the slint-lsp download
```

On macOS/Linux, `CreateVsixContainer` and `DeployExtension` are switched off because `VsixUtil.exe` only runs on Windows. Also on macOS, the release zips use `\` in entry names, so the bundled `licenses/*.md` files don't extract properly there; they do on Windows.

### Lessons learned
- Language-server document filters **must** be `DocumentTypeConfiguration`. `DocumentFilter.FromGlobPattern` fails with CEE0005 ("Language servers require specifying a document type"). Globs still work for text view listeners.
- The language-server, settings and output-pane APIs are still marked preview, so the build needs `NoWarn` for `VSEXTPREVIEW_LSP;VSEXTPREVIEW_SETTINGS;VSEXTPREVIEW_OUTPUTWINDOW`.
- The VS threading analyzers complain about `.Result` / awaiting tasks the code didn't start itself. Expose plain values instead (for example, `SlintServerProcess.ExitCode`).
- Contribution properties (`[VisualStudioContribution]`) are evaluated at compile time. Use `new[] { ... }` arrays, as the docs do.
- Extensibility packages used: `Microsoft.VisualStudio.Extensibility.Sdk` / `.Build` **17.14.40608**.

## Known gaps
- `slint-project.json` isn't sent to the server (there's no document type for that one file name).
- When the proxy handles `window/showDocument`, the file opens but the requested `selection` isn't applied.
- Commands aren't in the editor's right-click menu yet.
- Build warnings CEE0027 (25 of them): UI strings need moving into `.vsextension/string-resources.json`.
- `LICENSE` only states the licence expression and links to the upstream licence texts. Decide whether to include the full texts.

## Next steps

### Phase 0: checks on Windows + VS 2026 (do first)
1. F5 into the experimental instance and open a Slint template project, for example `slint-rust-template`.
2. Check: diagnostics, completion, go-to-definition, rename (including the server's follow-up rename prompt via `window/showMessageRequest` + `workspace/applyEdit`), code lens "▶ Show Preview" (runs a server command through `workspace/executeCommand`), document colours, semantic tokens, formatting.
3. Check that `bin/slint-lsp-*.exe` and `bin/licenses` end up in the VSIX. If not, adjust how they're included in the package.
4. Check that toggling `Enabled` false then true actually restarts the server (Restart command).
5. Check `window/showDocument`: is it handled by VS or by the proxy? Clicking in the preview should jump to the source.
6. Check that the "Slint" output pane gets the server's log, and that the settings page appears and changes take effect.
7. Check that the `rust` document type doesn't clash with other Rust extensions.

### Phase 4: grammar and snippets
- Copy from slint `v1.18.1`: `editors/vscode/slint.tmLanguage.json` (it's a symlink; find the real file), `language-configuration.json`, `slint.injection.json`, `slint.markdown-injection.json`, and `snippets/slint.json`.
- Register the grammars with a `.pkgdef` that adds a `TextMate\Repositories` entry pointing to `$PackageFolder$\Grammars`. Check that an out-of-process VSIX can include a `.pkgdef`.
- Check whether Visual Studio honours `injectTo` (Rust, Markdown).
- Snippets: try converting to `.snippet` XML; if VS can't attach them to a TextMate-only language, drop them (the server already offers snippet completions).
- Add a `.slint` file icon.

### Phase 6: extras
- **Docs command:** "Open Slint Docs" for the element under the caret. Port the `getHelpUrlForElement()` table and `helpBaseUrl()` from `editors/vscode/src/common.ts`. Base URL: `https://releases.slint.dev/<version>/docs/slint/reference/`.
- **New Project:** prompt for the language (Rust/C++/Node/Python templates, as in `quick_picks.ts`), pick a folder, `git clone` (or download the zip if git is missing), then open the folder.
- **Telemetry:**
  - count `LspProxy.TelemetryEventReceived` events (`preview_opened`, `property_changed`, `data_json_changed`, ...) in persisted state
  - send an hourly `live-preview-stats` summary and a one-off `extension-activated` event
  - report `lsp-panic` events from `PanicLog`
  - respect the VS telemetry consent, and add an opt-out setting
  - endpoint and format: see `editors/vscode/src/telemetry.ts`
- **Commands:** also place them in the editor's right-click menu with `CommandPlacement.VsctParent` (needs the right GUID/ID for the code window group).

### Phase 7: CI and publishing
- Repo: https://github.com/yurkinh/slint-visualstudio (branch `main`).
- GitHub Actions `windows-latest`: restore, build, run the tests, and keep the `.vsix` as a build artifact.
- Publish with `VsixPublisher.exe` and a publish manifest; marketplace token stored as a secret.
- A manual workflow to bump `SlintLspVersion`.

## Verification checklist (final)
- [ ] Unit tests pass (`dotnet test`).
- [ ] Grammar tests: `vscode-tmgrammar-test` against upstream `tests/grammar/*.slint`.
- [ ] VS 2026 x64 and ARM64:
  - [ ] highlighting, diagnostics, completion, go-to-definition, rename, code lens
  - [ ] Show Preview opens the native window and updates as you type
  - [ ] clicking in the preview jumps to the source
  - [ ] `includePaths` / `previewStyle` changes take effect without a restart
  - [ ] Restart command works
  - [ ] killing `slint-lsp.exe` shows the crash prompt and the panic text in the output pane
- [ ] With `attachToRust` on: a `.rs` file containing `slint!` starts the server. With it off: `.rs` files never reach the server.
