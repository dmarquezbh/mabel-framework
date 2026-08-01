# apple_device_manager.rb — gerenciar devices do Apple Developer (conta free)

## O problema

A conta **Personal Team** gratuita da Apple (sem os US$ 99/ano do Apple
Developer Program) tem um teto de dispositivos físicos registrados por
classe (ex.: iPhones). Ao configurar o Mabel num device novo depois de
bater esse teto, o build falha assim:

```
error: Communication with Apple failed: Your development team has reached
the maximum number of registered iPhone devices.
error: No profiles for '<bundle-id>' were found
```

A API pública da Apple **não tem delete de device** — só `PATCH .../devices/{id}`
com `status: DISABLED`. Mas desabilitar libera a quota de provisioning do
mesmo jeito que remover liberaria. É exatamente o que o portal
`developer.apple.com/account/resources/devices/list` faz quando você clica
em "Remove" — só que ali a UI **exige conta paga** pra mostrar a ação de
gerenciamento. Via API (a mesma que o Xcode usa internamente), funciona em
conta free.

Esse é um problema recorrente pra qualquer dev configurando o Mabel com
Personal Team gratuito — cedo ou tarde alguém vai bater no teto.

## Duas rotas neste repo

1. **`mabel apple devices`** (`src/Mabel.Cli`, ver `docs/mabel-apple-devices.md`
   e `docs/gerenciar-devices-apple-xtool.md`) — usa um `xtool` recompilado
   com um subcomando `set-status` (patch manual em Swift). Já validada
   ponta a ponta (2026-07-18), mas exige compilar o xtool a partir do
   fonte — trabalho considerável pra quem só quer liberar um slot.
2. **`apple_device_manager.rb`** (este script) — mesma ideia, mas usando a
   gem `spaceship` (parte do `fastlane`, madura e mantida pela comunidade
   de CI/CD iOS) em vez de compilar nada. Só precisa de
   `gem install spaceship`. Fala com o mesmo endpoint
   (`developerservices2.apple.com` / API `v1/devices`) que o xtool patch
   já comprovou funcionar em conta free.

Use a que fizer mais sentido: `mabel apple devices` se você já tem o xtool
compilado; este script se só quer resolver rápido sem compilar Swift.

## Uso

```bash
gem install spaceship   # uma vez

ruby tools/apple_device_manager.rb list
ruby tools/apple_device_manager.rb disable <UDID-ou-nome>
ruby tools/apple_device_manager.rb enable  <UDID-ou-nome>
```

- **Login é sempre interativo** — o script pede Apple ID, senha (via
  `IO.console.getpass`, não aparece no terminal) e, se a conta tiver 2FA
  (recomendado sempre ter), o código de 6 dígitos, através do prompt
  nativo do `spaceship`/`fastlane`.
- **Nada é salvo, logado ou enviado a terceiros.** Sem `.env`, sem cache de
  sessão em disco, sem flag de "lembrar senha". Cada execução autentica do
  zero. Isso é proposital — o custo é digitar o 2FA de novo a cada uso, a
  troca é não deixar nenhum segredo em disco.
- `disable` **sempre pede confirmação** (`y/N`) mostrando nome + UDID do
  device antes de agir, e nunca desabilita o device atualmente conectado
  se você passar `--keep-udid <UDID>` (ex.: o UDID do device que você está
  tentando instalar agora).
- A Apple não tem delete — `disable` é a ação real; `enable` reverte.

## Limitações conhecidas

- Precisa que o Apple ID já esteja associado a um team (mesmo free) — não
  cria conta nem aceita termos.
- 2FA por SMS pode não funcionar bem via CLI dependendo da conta; 2FA por
  app confiável (Xcode/iPhone) é o caminho testado pela comunidade
  fastlane/spaceship.
- **Este script específico ainda não foi validado contra a API real da
  Apple** neste ambiente (sessão sem acesso a Apple ID + 2FA de verdade).
  A abordagem replica exatamente o endpoint e o efeito já comprovados pelo
  patch do xtool (`docs/gerenciar-devices-apple-xtool.md`, validado em
  2026-07-18) — mas rode `list` primeiro e confira a saída antes de usar
  `disable` em produção.
