# Mabel — Roadmap de Finalização

> Framework de UI declarativa cross-platform (Server-Driven UI + guests WebAssembly)
> alvo iOS / Android / Windows / macOS / WASM. Este documento traça o caminho do
> estado atual (v1 consolidado) até produção.

Status: **Fase A concluída** nesta branch. Fases B–D planejadas.

Legenda: ✅ pronto/validado · 🟡 parcial/em progresso · 🟢 planejado

---

## Fase A — Consolidação (ESTA branch) ✅

Unificação de todas as branches de plataforma e do stack SDUI numa base única,
com os 3 projetos de teste que rodam em Linux verdes.

- ✅ Base SDUI (`feat/sdui-descriptor`) — descriptor schema + ADR 0001, view-builder iOS.
- ✅ Onda 1+2 do schema (`feat/mabel-roadmap-schema`) — versionamento + degradação graciosa,
  a11y, layout responsivo, listas virtualizadas, navegação/routing (ADRs 0008–0012),
  round-trip + compat OTA ATDD; iOS device-validado.
- ✅ Capabilities ABI (`feat/mabel-capabilities-abi`) — contrato WIT por capability
  (biometrics, bluetooth/BLE, camera, clipboard, haptics, location, notifications,
  secure-storage, share, streaming, permissions), host iOS nativo, harness xtool.
- ✅ Host Windows (`spike/host-windows`) — WPF desktop, consumo do schema v2, List virtualizada, NavStack.
- ✅ Host macOS (`feat/mabel-host-macos`) — build + assinatura ad-hoc sem Mac.
- ✅ Host Android (`feat/mabel-host-android`) — Jetpack Compose, schema v2, fallback type-200.
- ✅ .NET → core-wasm (`docs/dotnet-aot-wasm`) — toolchain NativeAOT-LLVM → wasm hostável no WasmKit.
- ✅ Arquitetura consolidada (`feat/mabel-arch-consolidation`) — README + ADRs 0003–0008 (HMR/estado,
  desktop, super-app, OTA, autoria poliglota, debugging), docs de OTA/super-app/offline.

**Validação (WSL, .NET 10.0.110, RID nativo linux-x64):**

| Projeto de teste          | Resultado          |
|---------------------------|--------------------|
| Mabel.Core.Tests          | 10/10 ✅           |
| Mabel.Renderer.Tests      | 26/26 ✅           |
| Mabel.Wasi.Protocol.Tests | 11/11 ✅           |
| **Total**                 | **47/47, 0 falhas**|

Hosts nativos (iOS/Android/Windows/macOS) não fazem parte de `Mabel.sln` e são validados
por suas próprias toolchains — ver seção de status por plataforma no PR.

---

## Fase B — Roadmap de recursos SDUI + DX 🟡🟢

Ampliar o descriptor e o runtime para paridade com um framework de UI de produção.

**Recursos de UI (schema + renderers por plataforma):** — Onda 🟡 entregue no schema
v3 (contrato em `Mabel.Wasi.Protocol/Sdui/*` + WIT `sdui@0.3.0`; host de referência
Windows/WPF renderizando; testes de round-trip/compat em `Mabel.Wasi.Protocol.Tests`).
- 🟡 **Theming** — tokens (cores/tipografia/espaçamento) + dark mode via `SduiThemeSet`/
  `SduiThemeResolver`; nós referenciam tokens (`*Token`/`TextStyle`). Falta: tema por-tenant.
- 🟡 **i18n / l10n** — strings externalizadas (`SduiLocalization`/`SduiLocalizer`), fallback
  em cadeia, interpolação `{arg}`, pluralização one/other. Falta: RTL, formatação numérica/data por locale.
- 🟡 **Animações / transições** — primitivas declarativas (`SduiAnimation`: fade/slide/scale/
  expand + easing/spring) + `SduiNavTransition`. Host WPF aplica fade/scale; falta paridade cross-host.
- 🟡 **Forms** — inputs (TextField/Select/Checkbox/Switch/Slider/Stepper) + validação declarativa
  (`SduiValidator`, regras required/len/pattern/min/max/email) + binding por `Field`. Falta: estado de submit assíncrono.
- 🟡 **Catálogo de componentes** — nós ampliados (TabBar/Grid/Sheet/Avatar/Chip + inputs). Falta:
  galeria/storybook por plataforma.
- 🟡 **Media** — `SduiMedia` (poster/autoplay/loop/controls/fit) + nós Video/Audio. Host WPF =
  placeholder. Falta: cache/lazy-load e player nativo real.
- 🟡 **Lifecycle** — hooks `onAppear`/`onDisappear` + Tabs + deep-link (rota nomeada + params, já na v2).
  Falta: restauração de estado, back-stack por-aba.

**Developer Experience:** — Onda 🟢 entregue (contrato puro em
`Mabel.Wasi.Protocol/DevTools/*` + `Sdui/ErrorBoundary.cs`; testado no WSL em
`Mabel.Wasi.Protocol.Tests`; host Windows com `--inspect` e error boundary).
- 🟢 **DevTools** — `SduiInspector`: dump navegável da árvore (texto `│├─` + JSON
  hierárquico) com tipos/ids/props/**tokens resolvidos** (tema ativo) + **texto
  localizado** (locale ativo) + estado inicial dos inputs; modo `--inspect`/
  `--inspect-json` no host Windows. Falta: overlay de estado ao vivo + time-travel.
- 🟢 **Testing** — `SduiSnapshot`: harness de snapshot **semântico** (não-pixel) do
  descritor resolvido, determinístico e cross-host, comparado com baseline
  versionado (`Snapshots/*.snap`, atualiza com `MABEL_UPDATE_SNAPSHOTS=1`). Falta:
  testes de contrato de capability.
- 🟢 **Error boundaries** — `SduiErrorBoundary`: fallback POR SUBÁRVORE — um nó que
  falha ao renderizar (probe do host lança / dados inválidos / tipo quebrado) é
  isolado num placeholder de erro sem derrubar os irmãos + telemetria
  (`ISduiErrorSink`); estende o placeholder tipo-200 pra qualquer exceção. Host
  Windows envolve cada `Build(nó)` no boundary.
- 🟢 **CI** — `.github/workflows/ci.yml` (GATE: 3 xUnit Linux + cross-build do host
  Windows + `tools/wit-lint.sh`; advisory: parse `wasm-tools`) +
  `native-hosts.yml` (Android/macOS/iOS best-effort, a validar no 1º run).

---

## Fase C — Guests poliglotas / .NET ao vivo 🟢

Executar lógica de aplicação como guests WebAssembly, em qualquer linguagem.

- 🟢 **.NET-live** — `[DllImport("mabel")]` + `[WasmImportLinkage]` para chamar o host a partir de
  guest .NET compilado a core-wasm (base provada em `docs/dotnet-aot-wasm`).
- 🟢 **Guests Go / Rust** — bindings gerados a partir do WIT das capabilities; TinyGo/Rust como cidadãos de 1ª classe.
- 🟢 **HMR simultâneo** — hot-reload de descriptor + módulo wasm em paralelo, multi-target (ADR 0003 HMR/estado).
- 🟢 **Bridge in-process** — evoluir o `InProcessGuestBridge` para runtime wasm real (WasmKit no iOS; wasmtime/wasmer nos desktops).

---

## Fase D — Produção 🟢

- 🟢 **OTA** — entrega de descriptor + módulo wasm via canal versionado, rollback, compat gates (ADR 0006).
- 🟢 **Offline / cache** — cache local de descriptor+assets, política stale-while-revalidate, modo offline (docs/offline).
- 🟢 **Super-app / mini-apps** — isolamento por mini-app, roteamento, sandbox de capabilities (ADR 0005, docs/super-app).
- 🟢 **Free-gating** — controle de acesso a features/capabilities por tier de licença.
- 🟢 **CD** — publicação automatizada por loja (App Store / Play / MS Store / notarização macOS) + release do canal OTA.

---

## Dependências entre fases

```
A (consolidação) ──> B (recursos + DX) ──> D (produção)
                └──> C (guests poliglotas) ──┘
```

B e C podem avançar em paralelo após A; D depende de ambas para o conjunto de produção completo.
