# mabel apple devices

Gerencia os devices registrados na conta Apple Developer (inclusive conta free)
direto do mabel CLI — sem precisar patchar o xtool na mao.

## Uso

```bash
mabel apple devices              # lista (id, nome, udid, status)
mabel apple devices list         # idem
mabel apple devices disable <id> # desabilita -> libera slot de provisioning
mabel apple devices enable <id>  # reativa
```

O `<id>` e o campo `id` da listagem (ex.: `348MCG364B`).

> A API da Apple nao tem delete de device — so disable. Mas DISABLED libera a
> quota de provisioning do mesmo jeito.

## Requisitos

- `list` funciona com o xtool de fabrica (>= 1.x), ja autenticado (`xtool auth`).
- `enable`/`disable` exigem um build do xtool com o subcomando
  `ds devices set-status` — receita completa em
  [gerenciar-devices-apple-xtool.md](gerenciar-devices-apple-xtool.md) (Linux/WSL)
  ou [gerenciar-devices-apple-xtool-macos.md](gerenciar-devices-apple-xtool-macos.md) (macOS).

## Auto-detecção no macOS

No macOS, se `MABEL_XTOOL` **não** estiver setada, o mabel procura sozinho um
build patcheado em `~/xtool-src-macos/.build/release/xtool` e
`~/xtool-src-macos/.build/debug/xtool` (nessa ordem) antes de cair no `xtool`
de PATH. Bastando seguir a receita macOS, `mabel apple devices enable/disable`
já funciona sem precisar exportar nada. No Linux/WSL essa auto-detecção não
existe — lá o binário patcheado só roda atrás do wrapper com
`LD_LIBRARY_PATH` (libxadi), então `MABEL_XTOOL` continua obrigatória.

## MABEL_XTOOL

Aponte para um build custom (aceita prefixo de comando completo, ex. wrapper
que seta `LD_LIBRARY_PATH`) — sempre tem prioridade sobre a auto-detecção:

```bash
export MABEL_XTOOL="$HOME/xtool-src/.build/debug/xtool"
mabel apple devices disable 348MCG364B
```

Se o binario nao suportar set-status, o mabel avisa e aponta pra doc da receita.
