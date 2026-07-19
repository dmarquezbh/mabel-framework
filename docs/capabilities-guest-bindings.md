# Mabel Capabilities — bindings do guest (core-wasm WASI p1)

> Assinaturas de import/export que o **guest** (WASM sandbox; guest-live é
> lean-lang, mas vale p/ qualquer linguagem → core-module) declara para falar
> com o host. Não precisa de guest .NET — é o contrato de wire achatado do
> ADR 0002 (one-shot) + 0003 (stream). O host iOS que os satisfaz está em
> `src/Mabel.Host.Ios/Sources/MabelHost/Capabilities/`.

## Convenções

- Todas as funções são **core WASM**; tipos = `i32`, `i64`, `f32`, `f64`.
- **Strings/records** passam como `(ptr: i32, len: i32)` em memória linear,
  serializados em **UTF-8 / JSON** (shapes = os records de `CapabilityTypes.swift`).
- `request-id` e `subscription-id` = `i64`, gerados pelo guest (espaços separados).
- Módulo de import = **`mabel:cap`**.
- Retorno das funções async/subscribe = `i32` = `CapStatus`
  (`0=ok,1=permission-denied,2=not-authorized,3=unavailable,4=cancelled,5=timeout,6=error`).

## Exports do guest (o host chama)

```wat
;; one-shot: um resultado por request-id
(func (export "mabel_on_capability_result")
  (param $req_id i64) (param $cap_id i32) (param $status i32)
  (param $payload_ptr i32) (param $payload_len i32))

;; stream: N eventos por subscription-id
(func (export "mabel_on_capability_event")
  (param $sub_id i64) (param $cap_id i32) (param $event_kind i32)
  (param $payload_ptr i32) (param $payload_len i32))

;; o host aloca no guest p/ escrever o payload do callback; guest libera depois
(func (export "cap_alloc") (param $len i32) (result i32))   ;; -> ptr
(func (export "cap_free")  (param $ptr i32) (param $len i32))
```

`cap_id` = `CapabilityId` (`0=camera,1=photo-library,2=location,3=notifications,
4=biometrics,5=secure-storage,6=share,7=clipboard,8=haptics,9=bluetooth`).

## Imports do host (o guest chama) — módulo `mabel:cap`

### Permissions
```wat
(import "mabel:cap" "cap_perm_check"   (func (param i32) (result i32)))          ;; (cap_id) -> permission-state
(import "mabel:cap" "cap_perm_request" (func (param i64 i32) (result i32)))      ;; (req_id, cap_id) -> status ; result-> payload[1]=state
```
`permission-state`: `0=not-determined,1=granted,2=denied,3=restricted`.

### Streaming genérico
```wat
(import "mabel:cap" "cap_unsubscribe" (func (param i64)))                        ;; (sub_id)
```

### Camera / photo-library
```wat
(import "mabel:cap" "cap_camera_capture"       (func (param i64 i32 i32) (result i32))) ;; (req_id, opts_ptr, opts_len) opts=CaptureOptions ; result payload=CapturedAsset
(import "mabel:cap" "cap_camera_pick"          (func (param i64 i32 i32) (result i32))) ;; (req_id, opts_ptr, opts_len) opts=PickerOptions
(import "mabel:cap" "cap_camera_read_asset"    (func (param i32 i32 i64 i32 i32) (result i32))) ;; (id_ptr,id_len, off:i64, len:i32, out_ptr) -> bytes_read [sync]
(import "mabel:cap" "cap_camera_release_asset" (func (param i32 i32)))           ;; (id_ptr, id_len)
```

### Location
```wat
(import "mabel:cap" "cap_location_get_current"       (func (param i64 i32) (result i32))) ;; (req_id, accuracy) one-shot ; result payload=Position
(import "mabel:cap" "cap_location_subscribe_updates" (func (param i64 i32) (result i32))) ;; (sub_id, accuracy) STREAM event-kind 0 payload=Position
```
`accuracy`: `0=coarse,1=balanced,2=precise`.

### Notifications (local)
```wat
(import "mabel:cap" "cap_notify_schedule"          (func (param i64 i32 i32) (result i32))) ;; (req_id, json_ptr, json_len) json=LocalNotification
(import "mabel:cap" "cap_notify_cancel"            (func (param i32 i32)))                  ;; (id_ptr, id_len)
(import "mabel:cap" "cap_notify_cancel_all"        (func))
(import "mabel:cap" "cap_notify_subscribe_received"(func (param i64) (result i32)))         ;; (sub_id) STREAM ev 0=tapped,1=received-foreground; payload=id(utf8)
```

### Biometrics
```wat
(import "mabel:cap" "cap_biometrics_available"    (func (result i32)))                      ;; -> 0=none,1=touch,2=face,3=optic [sync]
(import "mabel:cap" "cap_biometrics_authenticate" (func (param i64 i32 i32 i32) (result i32))) ;; (req_id, reason_ptr, reason_len, policy) ; result payload[1]=1/0
```
`policy`: `0=biometrics-only,1=biometrics-or-passcode`.

### Secure storage (síncrono)
```wat
(import "mabel:cap" "cap_secure_put"    (func (param i64 i32 i32 i32 i32 i32) (result i32))) ;; simplificado: (key_ptr,key_len, val_ptr,val_len, access, presence) -> status
(import "mabel:cap" "cap_secure_get"    (func (param i32 i32 i32) (result i32)))             ;; (key_ptr,key_len, out_ptr) -> status ; valor via cap_alloc
(import "mabel:cap" "cap_secure_delete" (func (param i32 i32) (result i32)))                 ;; (key_ptr,key_len) -> status
(import "mabel:cap" "cap_secure_keys"   (func (param i32) (result i32)))                     ;; (out_ptr) -> status ; JSON array via cap_alloc
```
`access`: `0=when-unlocked,1=after-first-unlock,2=when-passcode-set-this-device-only`.
(Nota: o `put` no lado Swift recebe `SecurePutOptions`; o encaixe exato dos params
do wire é do adapter de runtime — ver `CapabilityHost.securePut`.)

### Share
```wat
(import "mabel:cap" "cap_share_present" (func (param i64 i32 i32) (result i32))) ;; (req_id, json_ptr, json_len) json=[ShareItem] ; result payload[1]=completed
```

### Clipboard (síncrono)
```wat
(import "mabel:cap" "cap_clipboard_write_text" (func (param i32 i32) (result i32))) ;; (txt_ptr, txt_len) -> status
(import "mabel:cap" "cap_clipboard_read_text"  (func (param i32) (result i32)))     ;; (out_ptr) -> len (-1=none) ; texto via cap_alloc
(import "mabel:cap" "cap_clipboard_has_text"   (func (result i32)))                 ;; -> bool
```

### Haptics (fire-and-forget)
```wat
(import "mabel:cap" "cap_haptics_impact"       (func (param i32)))  ;; style 0=light,1=medium,2=heavy,3=soft,4=rigid
(import "mabel:cap" "cap_haptics_notification" (func (param i32)))  ;; kind 0=success,1=warning,2=failure
(import "mabel:cap" "cap_haptics_selection"    (func))
```

### Bluetooth (BLE central)
```wat
(import "mabel:cap" "cap_ble_state"                 (func (result i32)))                     ;; 0=unknown,1=unsupported,2=unauthorized,3=off,4=on [sync]
(import "mabel:cap" "cap_ble_start_scan"            (func (param i64 i32 i32) (result i32))) ;; (sub_id, filter_ptr, filter_len) STREAM ev0=device-found payload=BleAdvertisement
(import "mabel:cap" "cap_ble_connect"               (func (param i64 i32 i32) (result i32))) ;; (req_id, pid_ptr, pid_len) one-shot
(import "mabel:cap" "cap_ble_disconnect"            (func (param i32 i32)))                  ;; (pid_ptr, pid_len)
(import "mabel:cap" "cap_ble_discover"              (func (param i64 i32 i32) (result i32))) ;; (req_id, pid_ptr, pid_len) one-shot payload=BleGatt
(import "mabel:cap" "cap_ble_read_char"             (func (param i64 i32 i32 i32 i32) (result i32))) ;; (req_id, pid, uuid) one-shot payload=bytes
(import "mabel:cap" "cap_ble_write_char"            (func (param i64 i32 i32 i32 i32 i32 i32 i32) (result i32))) ;; (req_id, pid, uuid, val, with_resp)
(import "mabel:cap" "cap_ble_subscribe_char"        (func (param i64 i32 i32 i32 i32) (result i32))) ;; (sub_id, pid, uuid) STREAM ev1=char-changed payload=bytes
(import "mabel:cap" "cap_ble_subscribe_connection"  (func (param i64 i32 i32) (result i32))) ;; (sub_id, pid) STREAM ev2=connection-changed payload[1]=1/0
```

## Payloads JSON

Os shapes (camelCase) são os structs de `src/Mabel.Host.Ios/Sources/MabelHost/
Capabilities/CapabilityTypes.swift` — `CaptureOptions`, `PickerOptions`,
`CapturedAsset`, `Position`, `LocalNotification`, `ShareItem`, `BleScanFilter`,
`BleAdvertisement`, `BleGatt`, etc. Bytes inline (share blob, manufacturer data)
= **base64** em string.

> O **adapter de runtime** (WasmKit no iOS — pendente do spike #17 aterrissar no
> host) é quem decodifica os args `i32/i64/(ptr,len)` da tabela acima, lê a
> memória do guest via `GuestBridge`, e chama os métodos de `CapabilityHost`.
> Hoje o host + as impls nativas + o wire estão implementados e exercitados pelo
> **harness** (`samples/capabilities-harness/`) via `InProcessGuestBridge`.
