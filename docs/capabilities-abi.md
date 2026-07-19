# Mabel — ABI de Capabilities (acesso a APIs nativas do device)

> **Fase 2.** Contrato/design. Depende do renderer SDUI (fase 2, em `feat/sdui-descriptor`)
> e do spike WASM-on-device (WasmKit + xtool) provarem o caminho antes de implementar.
> Nada aqui compila lógica: são WIT + contratos de tipos + este doc + ADR 0002.

## 1. O problema

No stack Mabel, o app roda como **WASM sandbox** (.NET → wasm, executado no device
por WasmKit dentro do shell Swift). Sandbox = o guest **não enxerga** o SO. Ele só
fala com o host por **imports** (funções que o host injeta) e **exports** (funções
que o host chama). O canal de render já existe assim: `Protocol.cs` (modelo semântico)
↔ `WasiContract.cs` (nomes de função core `draw_rect`, `draw_text`…).

Este documento é o **irmão do render** para o resto do device: câmera, GPS, notificações,
biometria, keychain, share, clipboard, haptics. Modelo = **capability-based, estilo
WASI / Component Model**: o guest só recebe a autoridade que o app **declara** num
manifesto; o host media tudo.

Arquivos deste design:

```
src/Mabel.Wasi.Protocol/Capabilities/
  wit/                        # contrato semântico (north star, Component Model)
    world.wit                 # world mabel-capabilities (imports + o export callback)
    types.wit                 # request-id, cap-status, permission-state, capability-id
    permissions.wit           # check (sync) + request (async)
    camera.wit                # câmera + galeria + read-asset em chunks
    location.wit              # GPS one-shot + stream
    notifications.wit         # notificação LOCAL (push fica de fora — ver §5)
    biometrics.wit            # Face/Touch ID (veredito booleano)
    secure-storage.wit        # Keychain por-app (kv)
    share.wit                 # UIActivityViewController
    clipboard.wit             # UIPasteboard
    haptics.wit               # feedback tátil (fire-and-forget)
  CapabilityContract.cs       # lowering ACHATADO p/ core-module (irmão de WasiContract.cs)
  CapabilityManifest.cs       # modelo do manifesto (irmão de SduiDocument)
docs/
  capabilities-abi.md         # este doc
  adr/0002-capabilities-abi.md
```

## 2. Split: WASI padrão vs. custom

Regra: **não reinventar o que o WASI já padroniza.** O workload WASI do .NET
(`wasi-experimental` / target `wasi-wasm`) já linka os imports WASI Preview 1, então
essas capacidades "vêm de graça" e **não** entram neste pacote:

| Necessidade                | WASI padrão                                   | Provido por         |
|----------------------------|-----------------------------------------------|---------------------|
| Ler/gravar arquivos do app | `wasi:filesystem` (preopens do sandbox dir)   | SDK .NET (p1)       |
| Relógio / hora             | `wasi:clocks` (`wall-clock`, `monotonic`)     | SDK .NET (p1)       |
| Aleatoriedade / crypto rng | `wasi:random`                                 | SDK .NET (p1)       |
| Rede (HTTP/sockets)        | `wasi:http` / `wasi:sockets`                  | SDK .NET (p1/host)¹ |
| stdout/stderr, env, args   | `wasi:cli` (stdio, `environment`)             | SDK .NET (p1)       |

¹ Sockets em WASI p1 dependem do host expor o bridge; para chamadas de API o
caminho mais simples é o guest usar `HttpClient` sobre o `wasi:http` do host, ou
delegar ao host. **Fora do escopo deste design** (é rede, não device API).

**CUSTOM (o WASI não tem):** as interfaces WIT deste pacote. São "WASI-flavored"
(mesma gramática WIT, mesmo estilo de tipos), mas fora do namespace `wasi:` —
`package mabel:capabilities`. Cobrem exatamente o que o SO oferece e o WASI não
padroniza: **câmera, galeria, GPS, notificações locais, biometria, secure-storage,
share sheet, clipboard, haptics.**

## 3. Assincronia — request-id + callback (decidido; ADR 0002)

Chamada WASM é **síncrona e bloqueante**; APIs nativas (abrir câmera, esperar um
fix de GPS, prompt de Face ID) são **assíncronas e podem levar segundos**. Não dá
pra bloquear a thread do WASM esperando o usuário.

**Padrão escolhido — reqId + callback:**

```
guest                              host (Swift/WasmKit)
  │ cap_camera_capture(reqId, opts) ─────────►│  valida manifesto + permissão
  │◄──────────── CapStatus.Ok (aceito) ───────│  (retorna JÁ, não bloqueia)
  │                                            │  … abre AVCaptureSession (async) …
  │                                            │  usuário tira a foto
  │                                     ┌───────┤  cap_alloc(len) → ptr  (aloca no guest)
  │◄─── cap_alloc(len) ──────── ptr ────┘       │  copia payload p/ ptr
  │ mabel_on_capability_result(reqId,           │
  │   cam, Ok, ptr, len) ◄─────────────────────│  entrega resultado
  │ despacha por reqId → resolve a Task<>       │
  │ cap_free(ptr, len) ───────────────────────►│
```

- O guest gera `request-id` (u64) por chamada e guarda uma continuation
  (`TaskCompletionSource` no lado .NET). O callback único `on-capability-result`
  despacha por `(request-id, capability)` e completa a `Task`. Ergonomia async/await
  normal pro dev do app, apesar do wire ser callback.
- **Um único export de callback** (`mabel_on_capability_result`) em vez de um por
  capability: wire mínimo, casa com o lowering achatado.
- **Streams** (GPS contínuo) reusam o mesmo `request-id` em múltiplos callbacks até
  `stop-updates`.
- **Ownership de memória do payload:** host chama o export `cap_alloc` do guest,
  escreve os bytes, passa `(ptr,len)`; guest lê e chama `cap_free`. (Detalhe em
  `CapabilityContract.cs`.)

**Por que não futures do Component Model (`wasi:io/poll`, streams p2)?** Seria mais
elegante, mas exige Component Model + WASI Preview 2 **nas duas pontas**:
`componentize-dotnet` (NativeAOT-LLVM + wit-bindgen) no guest e suporte a componentes
no WasmKit no host — nenhum dos dois está sólido neste stack hoje. O WIT aqui é o
**contrato/north-star**; o transporte real é o core-module achatado, exatamente como
`Protocol.cs`↔`WasiContract.cs`. Migrar pra futures p2 depois é trocar o lowering
sem mudar o modelo semântico. **Ver ADR 0002 para a decisão completa.**

## 4. Segurança capability-based (duas camadas)

### Camada 1 — Manifesto (atenuação de autoridade, host-side)

O host **não liga nenhuma API nativa por padrão**. Ele lê o `CapabilityManifest`
(JSON no bundle, ex. `mabel.caps.json`) no load e só provê o import real das
capabilities **declaradas**. Capability não declarada → stub que responde
`CapStatus.NotAuthorized` na hora; o guest **nunca alcança o SO**.

```jsonc
{
  "schemaVersion": 1,
  "appId": "com.example.mabel.kanban",
  "grants": [
    { "capability": "camera",   "usageDescription": "Anexar foto do documento ao card." },
    { "capability": "location", "usageDescription": "Registrar onde a diligência foi feita.",
      "options": { "accuracy": "balanced" } },
    { "capability": "haptics" }
  ]
}
```

- **Least authority (POLA):** lista vazia = app puramente SDUI, zero device access.
  O que não está no manifesto é negado **por construção**, não por checagem esquecível.
- Estático, auditável, versionado com o app. É o que o host confia.
- O manifesto é a **fonte única** também para o build (ver §5): a `usageDescription`
  vira a usage-string do Info.plist.

### Camada 2 — SO / runtime (consentimento do usuário)

Mesmo declarada, câmera/GPS/notificações/biometria exigem o **prompt nativo** do iOS
("Permitir que o app use a câmera?"). Isso é intransponível pelo manifesto — é do SO.
A interface `permissions` (check síncrono + request assíncrono) expõe isso ao guest,
e todo resultado de capability pode voltar `permission-denied`.

Ordem recomendada no guest: `permissions.check` → se `not-determined`, `permissions.request`
no momento certo (com contexto de UI) → só então a chamada da capability.

## 5. Mapa iOS por capability

Cada capability → API nativa iOS + o gate de build (entitlement/Info.plist) + se o
**xtool consegue injetar** + se funciona na **conta Apple FREE** (`dmarquesbh@gmail.com`).

> **⚠️ Conta FREE (Personal Team) — limites que importam aqui:**
> perfis de 7 dias, **sem Push (APNs)**, **sem App Groups**, **sem Associated Domains**,
> **sem iCloud / iCloud Keychain**, **sem Keychain Sharing (access-groups)**. Vale a
> regra: capabilities que só precisam de **usage-string no Info.plist** passam na free
> (o xtool injeta a chave no plist gerado); capabilities que precisam de um **entitlement
> casado com App ID configurado no portal** NÃO passam na free.

| Capability      | API nativa iOS                                             | Gate de build (entitlement / Info.plist)                              | xtool injeta?        | Conta FREE |
|-----------------|------------------------------------------------------------|-----------------------------------------------------------------------|----------------------|------------|
| **camera**      | AVFoundation (`AVCaptureSession`) / `UIImagePickerController` | `NSCameraUsageDescription` (Info.plist)                              | Sim (chave plist)    | ✅ OK |
| **photo-library** | `PHPickerViewController` / `PHPhotoLibrary`              | `NSPhotoLibraryUsageDescription` (+ `…AddUsageDescription` p/ salvar) | Sim (chave plist)    | ✅ OK |
| **location**    | CoreLocation (`CLLocationManager`, when-in-use)            | `NSLocationWhenInUseUsageDescription` (Info.plist)                    | Sim (chave plist)    | ✅ OK |
| **notifications (local)** | UserNotifications (`UNUserNotificationCenter`)   | Nenhum entitlement; nenhuma usage-string obrigatória                  | N/A                  | ✅ OK |
| **notifications (push/remota)** | UserNotifications + **APNs**                | `aps-environment` entitlement + capability Push no App ID + provisioning | ❌ (precisa portal/App ID) | ❌ **BLOQUEADO** |
| **biometrics**  | LocalAuthentication (`LAContext.evaluatePolicy`)           | `NSFaceIDUsageDescription` (Info.plist; Touch ID não exige)           | Sim (chave plist)    | ✅ OK |
| **secure-storage (por-app)** | Security (`SecItem*` / Keychain)              | Nenhum entitlement p/ keychain básico por-app                         | N/A                  | ✅ OK |
| **secure-storage (compartilhado/iCloud)** | Keychain com groups / sync         | `keychain-access-groups` e/ou iCloud Keychain (App ID no portal)      | ❌ (precisa portal)  | ❌ **BLOQUEADO** |
| **share**       | `UIActivityViewController`                                 | Nenhum                                                                | N/A                  | ✅ OK |
| **clipboard**   | `UIPasteboard.general`                                     | Nenhum                                                                | N/A                  | ✅ OK |
| **haptics**     | `UIFeedbackGenerator` / `CoreHaptics`                      | Nenhum (só device físico; simulador ignora)                           | N/A                  | ✅ OK |

**Resumo pro escopo v2 na conta free:** câmera, galeria, GPS (when-in-use), notificação
**local**, Face/Touch ID, keychain **por-app**, share, clipboard e haptics **passam**.
Ficam **fora** (exigem conta paga / App ID no portal): **push notifications**, **keychain
compartilhado/iCloud**, e por tabela também App Groups e Associated Domains (não são
capabilities deste design, mas caem na mesma trave). Por isso `notifications.wit` expõe
**só local** e `secure-storage.wit` **só por-app**.

### Pontos a confirmar no spike (não assumir)

Marcados porque dependem do comportamento real do **xtool** e do runtime, que só o
spike WASM-on-device fecha:

1. **Injeção de Info.plist pelo xtool** — assumo que `xtool.yml` deixa declarar
   `Info.plist` custom (usage-strings). **Confirmar** a chave/sintaxe exata no xtool
   que está no repo (o hello-world já gera um Info.plist — ver `samples/hello-world-ios/`).
2. **`NSLocationWhenInUseUsageDescription` vs. Always** — v2 fica em when-in-use.
   Background location (`UIBackgroundModes: location`) provavelmente **não** vale a
   dor nem passa review; deixado fora. **Confirmar** se algum caso de uso exige.
3. **Permissão de câmera no simulador** — o simulador não tem câmera; teste real só
   no device. Alinhado com o deploy Mabel (sempre device físico).
4. **CoreHaptics vs. UIFeedbackGenerator** — os padrões comuns (impact/notification/
   selection) bastam para v2; CoreHaptics custom fica para depois.

## 6. Escopo / não-metas (v2)

- **É:** contrato WIT das 9 capabilities custom, lowering achatado core-module, modelo
  de manifesto capability-based, padrão async reqId+callback, mapa iOS + conta free.
- **Não é (ainda):** implementação do host Swift, bindgen .NET, push notifications,
  keychain compartilhado/iCloud, background location, CoreHaptics custom, host Android
  (a estrutura existe; o mapa Android — CameraX/FusedLocation/etc. — vem depois),
  transporte binário / migração pra futures do Component Model (troca de lowering futura).
```
