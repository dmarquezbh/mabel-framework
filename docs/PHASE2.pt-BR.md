# Roadmap da Fase 2

> [Read in English](PHASE2.md)

Este documento especifica a Fase 2 do Mabel Framework — funcionalidades planejadas apos o pipeline de renderizacao core estar estavel.

## Visao Geral

A Fase 2 foca em tres pilares:

1. **GitHub Pages** — Site do projeto com landing page e documentacao
2. **Playground no Navegador** — Programe e visualize apps Mabel direto no browser
3. **Assistente IA** — LLM para ajudar a construir UIs, disponivel via nuvem ou inferencia local
4. **Bot Telegram** — Converse com o assistente IA pelo Telegram
5. **Acesso a APIs Nativas** — WASI Capability Providers para APIs do dispositivo (camera, GPS, etc.)

---

## 1. GitHub Pages — Site do Projeto

### Stack

**Blazor WebAssembly** (dogfooding — o proprio site e construido com a mesma tecnologia do framework).

### Paginas

- **Landing page** — Bonita, animada, explica o que e o Mabel e por que importa
- **Getting Started** — Tutorial interativo
- **Referencia de API** — Gerada a partir dos XML docs
- **Playground** — Veja secao 2 abaixo
- **Blog** — Release notes, tutoriais, decisoes de arquitetura

### Hospedagem

GitHub Pages (arquivos estaticos). Blazor WASM compila para HTML/JS/WASM estaticos — sem servidor.

---

## 2. Playground no Navegador

### Objetivo

O usuario pode escrever um app Mabel **inteiramente no navegador** e ver renderizado em tempo real. Sem servidor, sem instalacao, sem backend — tudo roda client-side via WASM.

### Arquitetura

```
Navegador
  |
  +-- Monaco Editor (JS) -- edicao de codigo com syntax highlighting
  |
  +-- Roslyn (via .NET WASM) -- compila C# para IL
  |
  +-- Mabel Renderer (WASM) -- interpreta RenderCommands
  |
  +-- Preview Canvas (HTML5 Canvas / SVG) -- saida visual
```

### Abordagem Tecnica

1. **Monaco Editor** — Embutido via JS interop no Blazor. Fornece syntax highlighting de C#. IntelliSense completo e adiado (requer carregar ~50-100MB de servicos de linguagem do Roslyn).

2. **Compilacao** — Abordagem em duas camadas:
   - **Modo rapido**: Templates pre-compilados. Usuario modifica parametros (cores, texto, layout) e ve mudancas instantaneamente. Sem necessidade do Roslyn.
   - **Modo completo**: Compilador Roslyn carregado no browser via .NET WASM. Usuario escreve C# arbitrario que produz `RenderCommand[]`. Compilacao leva 2-5 segundos. Download inicial: ~50-100MB de assemblies do compilador (cacheado apos primeiro carregamento).

3. **Renderizacao** — Uma implementacao JavaScript/Canvas2D do `ICanvas` que renderiza `RenderCommand[]` diretamente no browser. Este e um novo backend de canvas (assim como `MabelCanvasView.swift` e para iOS).

4. **Sem servidor**: Tudo roda client-side. O IL compilado executa no runtime .NET WASM ja carregado pelo Blazor.

### Restricoes Realistas

- **Tamanho do download**: Primeira visita requer baixar o runtime .NET WASM (~15MB) + compilador Roslyn (~50-100MB para modo completo). Carregamento progressivo com modo rapido disponivel imediatamente.
- **Tempo de compilacao**: 2-5 segundos para programas simples no modo completo. Modo rapido e instantaneo.
- **Memoria**: WASM no browser tem limite de 4GB. Suficiente para o playground.
- **Compilacao Razor**: Nao e viavel client-side (requer Razor SDK completo + Roslyn + todos os assemblies de referencia = 100-200MB+). O playground compila C# puro que produz RenderCommands, nao arquivos `.razor`.

---

## 3. Assistente IA

### Objetivo

Ajudar usuarios a construir UIs Mabel descrevendo o que querem em linguagem natural. A IA gera codigo `RenderCommand[]` ou layouts completos de componentes.

### Dois Modos

#### Modo Cloud (Recomendado)

Usuario fornece sua propria chave de API para:
- **GitHub Copilot** (via token GitHub)
- **Anthropic Claude** (chave de API)
- **OpenAI** (chave de API)
- **Qualquer API compativel com OpenAI** (endpoint customizado + chave)

Chaves sao armazenadas **apenas no navegador** (localStorage). Nunca enviadas para nenhum servidor Mabel — o navegador chama a API do LLM diretamente (se CORS permitir) ou atraves de um proxy minimo que nao registra requisicoes.

#### Modo Local (Experimental)

Um LLM pequeno rodando **inteiramente no navegador** via WebGPU.

**Abordagem**: Usar [web-llm](https://github.com/mlc-ai/web-llm) (MLC AI) — o runtime de LLM no browser mais maduro. Usa WebGPU para inferencia acelerada por GPU.

**Modelos viaveis**:
- Qwen2-0.5B (~50+ tok/s em GPU moderna)
- Phi-3-mini 3.8B (~20-40 tok/s em GPU dedicada)
- TinyLlama 1.1B (~30-40 tok/s)

**Requisitos**: Navegador com suporte a WebGPU (Chrome 113+, Edge 113+). Faz fallback para modo cloud se WebGPU nao esta disponivel.

**Avaliacao realista**: Modo local com modelos pequenos (0.5B-1B) consegue lidar com tarefas simples como "mude o fundo para azul" ou "adicione um botao embaixo". Layouts complexos com multiplos componentes requerem modelos maiores (3B+) ou modo cloud. Isso e experimental — a qualidade vai melhorar conforme os modelos evoluem.

### LLMs de 1-Bit (Pesquisa Futura)

BitNet b1.58 (pesos ternarios: {-1, 0, +1}) oferece tamanhos de modelo dramaticamente menores:
- Modelo 0.7B = ~125MB (vs ~600MB para Q4 padrao)
- Modelo 2.4B = ~400MB (vs ~1.5GB para Q4 padrao)

**Status atual**: Nao existe runtime de navegador para inferencia 1-bit. [BitNet.cpp](https://github.com/microsoft/BitNet) (Microsoft) e apenas codigo nativo para CPU. Kernels WebGPU para dequantizacao de pesos ternarios precisariam ser criados do zero.

**Caminho a seguir**: Se/quando web-llm ou outro projeto adicionar suporte a BitNet b1.58, isso permitiria modelos maiores no browser com menor custo de memoria. Monitoramos esse espaco e integraremos quando for viavel.

---

## 4. Bot Telegram

### Objetivo

Desenvolvedores podem conversar com a IA do Mabel pelo Telegram — fazer perguntas, gerar codigo de UI, obter ajuda com o framework.

### Arquitetura

```
Telegram Bot API
      |
      v
Servidor do Bot (.NET leve ou funcao serverless)
      |
      +-- Usuario envia mensagem: "Crie uma tela de login"
      |
      v
Provedor LLM (provedor configurado pelo usuario)
      |
      v
Bot responde com codigo RenderCommand[] gerado
```

### Implementacao

- **Telegram Bot API** — Bot padrao usando pacote NuGet `Telegram.Bot` ou HTTP direto
- **Roteamento LLM** — Usuario configura seu provedor LLM via comandos do bot (`/config provedor anthropic`, `/config chave sk-...`)
- **Chaves armazenadas no servidor** — Encriptadas, por usuario. Ou usuario pode usar o comando `/ask` com chave inline (chave nao armazenada)
- **Saida de codigo** — Bot responde com blocos de codigo C# formatados que o usuario pode colar no seu projeto Mabel
- **Pode rodar em serverless** — Azure Functions, AWS Lambda, ou qualquer container

### Avaliacao Realista

Esta e a funcionalidade mais simples da Fase 2. Um bot Telegram que faz proxy para uma API de LLM e direto. O trabalho principal e engenharia de prompt para gerar bom codigo especifico para Mabel.

---

## 5. WASI Capability Providers — Acesso a APIs Nativas

### Objetivo

Permitir que apps Mabel acessem APIs do dispositivo (camera, GPS, notificacoes, sensores, etc.) atraves de uma interface limpa e cross-platform — sem o desenvolvedor criar pacotes nativos manualmente.

### Arquitetura

```
Guest (WASM)                              Host (Nativo)
  |                                          |
  | wasi_capability_request("camera.capture", params)
  |----------------------------------------->|
  |                                          |
  |                    Swift: AVFoundation   |
  |                    Kotlin: Camera2 API   |
  |                    Desktop: OS API       |
  |                                          |
  |<-----------------------------------------|
  | wasi_capability_response(result)         |
```

### Como Funciona

1. **Lado guest**: O modulo WASM chama `wasi_capability_request(capability, params)` — uma funcao WASI exportada
2. **Lado host**: O host nativo resolve o nome da capability para uma implementacao nativa
3. **Swift (iOS)**: Usa pacotes Swift Package Manager (SPM) existentes da comunidade. O host fornece uma camada fina de binding que delega para pacotes SPM
4. **Kotlin (Android)**: Mesmo padrao, usando pacotes Gradle/Maven
5. **Desktop (.NET)**: Chamadas diretas de API .NET

### Registro de Capabilities

```json
{
  "capabilities": {
    "camera.capture": {
      "ios": "AVFoundation",
      "android": "Camera2",
      "desktop": "System.Drawing"
    },
    "location.current": {
      "ios": "CoreLocation",
      "android": "FusedLocationProvider",
      "desktop": "GeoCoordinateWatcher"
    },
    "notification.send": {
      "ios": "UserNotifications",
      "android": "NotificationManager",
      "desktop": "ToastNotification"
    }
  }
}
```

### Por que SPM (Swift Package Manager)?

Para iOS, ao inves de criar pacotes do zero, aproveitamos o ecossistema SPM existente:

- Milhares de pacotes ja disponiveis
- Frameworks oficiais do SDK da Apple (AVFoundation, CoreLocation, etc.) nao precisam de pacotes extras
- O host Mabel ja e um Swift Package — adicionar dependencias SPM e trivial
- Pacotes da comunidade para features complexas (ex: Firebase, Stripe) podem ser adicionados como deps SPM

O host Mabel nao reinventa a roda — ele fornece **bindings** que delegam para pacotes existentes.

### Capabilities Planejadas (v1)

| Capability | iOS | Android | Desktop |
|-----------|-----|---------|---------|
| `camera.capture` | AVFoundation | Camera2 | - |
| `camera.picker` | UIImagePickerController | Intent.ACTION_PICK | FileDialog |
| `location.current` | CoreLocation | FusedLocationProvider | - |
| `notification.local` | UserNotifications | NotificationManager | ToastNotification |
| `haptics.impact` | UIImpactFeedbackGenerator | Vibrator | - |
| `share.text` | UIActivityViewController | Intent.ACTION_SEND | - |
| `clipboard.copy` | UIPasteboard | ClipboardManager | Clipboard |
| `storage.keyvalue` | UserDefaults | SharedPreferences | Preferences |
| `biometric.auth` | LocalAuthentication | BiometricPrompt | - |

---

## Cronograma

| Funcionalidade | Complexidade | Status |
|---------------|-------------|--------|
| GitHub Pages (landing) | Media | Planejado |
| Playground (modo rapido) | Media | Planejado |
| Playground (Roslyn completo) | Alta | Planejado |
| IA (modo cloud) | Baixa | Planejado |
| IA (local/WebGPU) | Alta | Pesquisa |
| Bot Telegram | Baixa | Planejado |
| WASI Capabilities (v1) | Alta | Planejado |
| Inferencia 1-bit LLM no browser | Muito Alta | Pesquisa |

---

## Decisoes Tecnicas

### Por que Blazor WASM para o site?

Dogfooding. O site do projeto construido com a mesma tecnologia demonstra que Blazor WASM funciona para aplicacoes reais. Tambem significa que contribuidores trabalhando no site estao aprendendo a mesma stack usada pelo framework.

### Por que nao compilacao server-side para o playground?

A filosofia do Mabel e **sem dependencia de servidor**. O playground deve funcionar offline, no aviao, sem internet. Compilacao server-side e mais rapida e facil, mas vai contra os valores do projeto. Aceitamos o tradeoff de download inicial maior pela independencia total client-side.

### Por que permitir provedores LLM na nuvem?

Modelos locais (0.5-3B parametros) produzem saida de qualidade inferior aos modelos cloud (70B+). Para geracao de UI com qualidade de producao, modelos cloud sao significativamente melhores. A opcao cloud respeita a privacidade do usuario usando suas proprias chaves e nunca roteando pelos servidores do Mabel.
