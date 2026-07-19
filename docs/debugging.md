# Mabel — Debugging & DevTools

> **Pilar (onda 4 do roadmap).** Hoje o debug real é primitivo (NSLog via
> `idevicesyslog`); esta é a visão do maduro. Decisão em `docs/adr/0008-debugging.md`.

## 1. Debug é multi-camada

O app Mabel tem quatro fronteiras distintas; cada uma tem sua ferramenta:

1. **Lógica (guest WASM):** debuga no **desktop / build-host** (runtime full, debugger
   normal da linguagem). A lógica é **a mesma** que roda no device — então provar no
   desktop cobre o grosso. O guest interpretado on-device tem debug limitado (o
   interpretador pode expor trace, mas não é um debugger completo).
2. **Descritor (árvore SDUI):** um **inspector de descritor** — o análogo do React
   DevTools / Flutter widget inspector: mostra a árvore de nós, props ao vivo, diff entre
   frames, e **time-travel** (histórico de descritores). Como o descritor é dado puro
   (ADR 0001), inspecioná-lo é trivial.
3. **Render nativo:** um **"select mode"** — toca numa view nativa e o inspector mostra o
   **nó SDUI de origem** (`Id` semântico). A ponte inversa do view-builder.
4. **Fronteira guest↔host (o wire):** um **wire inspector** — a "aba Network" do protocolo:
   descritores emitidos, eventos de tap, e chamadas de capability (com `reqId`/streams
   traçados, ADR 0002). Vê exatamente o que atravessou o sandbox.

## 2. Vantagens Mabel-específicas

O design do Mabel dá ferramentas de debug que caem de graça da arquitetura:

- **Web-host + DevTools do browser = superfície primária de debug.** Como o **mesmo
  descritor** roda no web e no nativo (via HMR multi-alvo, ADR 0003), o dev debuga no
  **Chrome DevTools** — fiel ao comportamento nativo (mesma árvore/estado/eventos),
  reusando um tooling maduro que ninguém precisa reconstruir. É o maior atalho de DX.
- **Replay determinístico.** O app é **descritor + WASM-DLL** com **estado externalizado**
  (ADR 0003). Capturando o descritor + o estado + a sequência de eventos do usuário, dá
  pra **re-executar** no browser/desktop e **reproduzir o bug a partir do dado** — sem
  "não consigo reproduzir". (Casa com o replay determinístico do modelo Elm/TEA.)
- **Error boundaries.** Um erro de nó ou de guest **isola no mini-app / subárvore** (não
  derruba o super-app — ADR 0005), com **overlay de erro** no dev. O sandbox WASM já dá a
  fronteira de falha; o boundary a apresenta.

## 3. Produção

- **Logging estruturado** — os `NSLog [Board] open-card card:X` de hoje são o embrião;
  o maduro é log estruturado (níveis, contexto, correlação por `reqId`).
- **Crash/telemetria** — New Relic (stack de observability padrão) + captura remota do
  **descritor + estado** no momento do erro (alimenta o replay determinístico).

## 4. Status (honesto)

| | Hoje | Alvo (onda 4) |
|---|---|---|
| Lógica | debugger no desktop | idem + trace on-device |
| Descritor | — | inspector (árvore/props/diff/time-travel) |
| Render | — | select-mode (view↔nó) |
| Wire | `NSLog` cru via `idevicesyslog` | wire inspector (descritores/eventos/capabilities) |
| Repro | manual | replay determinístico (descritor+estado+eventos) |
| Falhas | crash derruba tudo | error boundaries + overlay |
| Prod | `NSLog` | log estruturado + New Relic + captura remota |

**Onde estamos:** o debug que validou o tap no device foi **`NSLog` via `idevicesyslog`** —
primitivo, mas suficiente pra prova. O maduro é 🟢-tier, **onda 4** do roadmap (task #20).

## 5. Escopo / não-metas

- **É:** o modelo de debug em 4 camadas; as vantagens (web-devtools, replay, boundaries);
  o mapa hoje-vs-alvo; a estratégia de prod.
- **Não é (ainda):** implementação do inspector/wire/select-mode/replay/boundaries — tudo
  onda 4, depende do host multi-módulo + integração WASM-live + host web existirem.
