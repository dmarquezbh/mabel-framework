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

**iOS e Android são alvos CO-IGUAIS.** O contrato — WIT + descritor + manifesto — é
**platform-neutral por design**: um único WIT, consumido por dois hosts nativos independentes
(shell Swift no iOS, shell Kotlin/Java no Android). O guest .NET→WASM é o mesmo binário nos
dois. O que muda é só o host: cada um mapeia as MESMAS capabilities pras APIs nativas da sua
plataforma (§5) e roda o WASM no runtime que a plataforma permite (§3.1). A segurança
capability-based (declara→liga) vale igual nos dois.

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

> O diagrama acima mostra o host iOS (Swift/WasmKit); no Android o fluxo é **idêntico**
> — só troca o runtime WASM e a API nativa (`ACTION_IMAGE_CAPTURE`/CameraX no lugar do
> `AVCaptureSession`). Mesmos nomes de função core, mesmo callback.

### 3.1 Runtime WASM por plataforma (iOS sem JIT, Android com JIT)

Os dois hosts consomem o **mesmo WIT** e rodam o **mesmo guest**, mas o runtime WASM
difere pela política de cada SO:

| Plataforma | JIT? | Runtime WASM sugerido                                   | Nota |
|------------|------|--------------------------------------------------------|------|
| **iOS**    | ❌ proibido (sem memória executável gravável p/ apps de 3º) | **WasmKit** (interpretador, Swift puro) | Único caminho viável; sem AOT/JIT. Perf = interpretador. |
| **Android**| ✅ permitido | **wasmtime via JNI** (Cranelift JIT/AOT) — o mais rápido; **Chicory** (puro-Java, interpretador) como fallback sem NDK | Pode usar JIT → guest roda bem mais rápido que no iOS. |

Isso **não vaza pro contrato**: WIT, manifesto e os nomes de função core são idênticos.
O host Android é um projeto separado (`Mabel.Host.Android`, Kotlin/Java) do host iOS, mas
os dois implementam a mesma ABI. A escolha de runtime é interna ao host — o guest não sabe
nem se importa. (No iOS, a perf de interpretador do WasmKit é o motivo extra pra manter
payloads pequenos e mídia via `read-asset` em chunks, não despejada na memória linear.)

## 4. Segurança capability-based (duas camadas)

### Camada 1 — Manifesto (atenuação de autoridade, host-side)

O host **não liga nenhuma API nativa por padrão**. Ele lê o `CapabilityManifest`
(JSON no bundle, ex. `mabel.caps.json`) no load e só provê o import real das
capabilities **declaradas**. Capability não declarada → stub que responde
`CapStatus.NotAuthorized` na hora; o guest **nunca alcança o SO**.

```jsonc
{
  "schemaVersion": 1,
  "appId": "com.pjus.mabel.board",
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
- O manifesto é a **fonte única** também para o build, nas **duas plataformas** (ver §5):
  do `CapabilityId` + `usageDescription` o passo de build deriva as entradas nativas —
  no **iOS** a usage-string do `Info.plist` (ex.: `NSCameraUsageDescription`); no
  **Android** o `<uses-permission>` no `AndroidManifest.xml` (ex.: `android.permission.CAMERA`)
  e a string de rationale do prompt runtime. O manifesto Mabel é neutro; o mapa
  `CapabilityId → permissão nativa` é responsabilidade de cada host (§5).

### Camada 2 — SO / runtime (consentimento do usuário)

Mesmo declarada, câmera/GPS/notificações/biometria exigem o **prompt nativo do SO**
("Permitir que o app use a câmera?") — vale igual no iOS e no Android (runtime permissions
do Android 6+). Isso é intransponível pelo manifesto — é do SO. A interface `permissions`
(check síncrono + request assíncrono) expõe isso ao guest de forma platform-neutral, e todo
resultado de capability pode voltar `permission-denied`.

Ordem recomendada no guest: `permissions.check` → se `not-determined`, `permissions.request`
no momento certo (com contexto de UI) → só então a chamada da capability.

## 5. Mapa por capability — iOS **e** Android

Cada capability mapeia pra uma API nativa em **cada** plataforma, com o respectivo gate
de permissão. O contrato (WIT/manifesto) é o mesmo; só o host traduz.

### 5.1 iOS

API nativa iOS + gate de build (entitlement/Info.plist) + se o **xtool consegue injetar**
+ se funciona na **conta Apple FREE** (`dmarquesbh@gmail.com`).

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

### 5.2 Android

API nativa Android + a permissão declarada no `AndroidManifest.xml` (as marcadas
*(runtime)* também exigem o prompt de runtime do Android 6+) + se precisa de algo extra
(Play Services / Play Console).

> **Notas de plataforma (Android):**
> - Permissões `normal` (VIBRATE, USE_BIOMETRIC) são **auto-concedidas** no install; as
>   `dangerous` (CAMERA, localização, POST_NOTIFICATIONS) exigem prompt **runtime**.
> - **Play Console** só entra ao **publicar na Play Store** (formulário de Data Safety;
>   declaração de background-location/foreground-service). No fluxo Mabel (sideload do
>   APK, análogo ao xtool no iOS) **não é necessário** pra dev/teste.
> - **Sem trava de "conta free"** como no iOS: sideload de APK assinado com debug/keystore
>   próprio é livre. Inclusive **push (FCM) é grátis** no Android — a exclusão de push do
>   v2 é uma escolha de **paridade com o iOS**, não um limite do Android (ver tabela).

| Capability      | API nativa Android                                          | `AndroidManifest.xml` (permissão)                                       | Extra |
|-----------------|-------------------------------------------------------------|-------------------------------------------------------------------------|-------|
| **camera**      | CameraX (androidx.camera) / `MediaStore.ACTION_IMAGE_CAPTURE` | `android.permission.CAMERA` *(runtime)* + `<uses-feature android.hardware.camera>` | — |
| **photo-library** | **Photo Picker** (`PickVisualMedia`, API 33+) / SAF `ACTION_OPEN_DOCUMENT` | Photo Picker/SAF = **sem permissão**; legado: `READ_MEDIA_IMAGES`/`READ_MEDIA_VIDEO` (33+) ou `READ_EXTERNAL_STORAGE` *(runtime)* | Prefira Photo Picker (permissionless) |
| **location**    | `FusedLocationProviderClient` (Play Services) / `LocationManager` (AOSP) | `ACCESS_FINE_LOCATION` / `ACCESS_COARSE_LOCATION` *(runtime)*            | Fused precisa Play Services; AOSP não. Background = `ACCESS_BACKGROUND_LOCATION` (fora do v2) |
| **notifications (local)** | `NotificationManagerCompat` + `NotificationChannel` (API 26+) | `POST_NOTIFICATIONS` *(runtime, API 33+)*                           | — |
| **notifications (push/remota)** | **FCM** (Firebase Cloud Messaging)               | `POST_NOTIFICATIONS` *(runtime)*                                        | Precisa projeto **Firebase** (grátis). ✅ **funciona no Android** (≠ iOS free) — cortado do v2 só por paridade |
| **biometrics**  | `BiometricPrompt` (androidx.biometric)                      | `USE_BIOMETRIC` *(normal, auto)*                                        | — |
| **secure-storage (por-app)** | Android Keystore + `EncryptedSharedPreferences` / DataStore | **Nenhuma permissão**                                          | Chaves lastreadas no Keystore (hardware-backed onde houver) |
| **secure-storage (sync)** | Block Store / Backup                              | **Nenhuma permissão**                                                   | Sync entre devices = Block Store (fora do v2, igual iCloud) |
| **share**       | `Intent.ACTION_SEND` / `ACTION_SEND_MULTIPLE` + `createChooser` | **Nenhuma permissão**                                              | — |
| **clipboard**   | `ClipboardManager` (`CLIPBOARD_SERVICE`)                    | **Nenhuma permissão**                                                   | Leitura só com app em foreground (API 29+); toast de cópia (API 33+) |
| **haptics**     | `VibratorManager` (API 31+) / `Vibrator` + `VibrationEffect`; `View.performHapticFeedback` | `android.permission.VIBRATE` *(normal, auto)*             | Só device físico |

**Resumo Android v2:** as mesmas 9 capabilities do iOS mapeiam limpo. Nenhuma trava de conta;
o único "extra" é Firebase pra push (fora do v2) e Play Services pra localização *fused*
(dá pra cair no `LocationManager` da AOSP se quiser zero Google). Paridade com o iOS
mantida: `notifications` = **local**, `secure-storage` = **por-app**.

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
5. **Android — runtime WASM:** confirmar no spike se **wasmtime via JNI** (Cranelift)
   builda/roda limpo no `Mabel.Host.Android` ou se começamos com **Chicory** (puro-Java,
   sem NDK, mais simples porém interpretado). Não muda o contrato — decisão interna do host.
6. **Android — localização:** `FusedLocationProviderClient` exige Play Services. Confirmar
   se aceitamos a dependência do Google ou se caímos no `LocationManager` da AOSP (sem Google).

## 6. Escopo / não-metas (v2)

- **É:** contrato WIT das 9 capabilities custom (platform-neutral), lowering achatado
  core-module, manifesto capability-based, async reqId+callback, mapa **iOS + Android**
  por capability, nota de runtime (iOS interpretador / Android JIT).
- **Não é (ainda):** implementação dos hosts (Swift **e** Kotlin/Java), bindgen .NET, push
  notifications (APNs/FCM), keychain compartilhado/iCloud/Block Store, background location,
  CoreHaptics custom, transporte binário / migração pra futures do Component Model.
- **Os dois hosts são co-iguais no design.** A implementação pode priorizar o iOS primeiro
  (onde o Board é a prova), mas o contrato já nasce válido pros dois — nenhum retrabalho de
  ABI pra ligar o Android depois.
```
