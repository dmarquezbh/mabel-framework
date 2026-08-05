# ADR 0013 — Hosts nativos distribuídos como pacote binário (AAR / NuGet), não módulo-fonte

- **Status:** Proposto (design de plataforma)
- **Data:** 2026-08-05
- **Irmão de:** ADR 0002 (capabilities ABI — hosts nativos co-iguais), ADR 0005 (super-app).
  Complementa a promessa do README ("o shell nativo pode ser embutido em qualquer app") com
  o mecanismo real de consumo.

## Contexto

O pitch central do Mabel é que o **host nativo** (`Mabel.Host.Android`, `Mabel.Host.iOS`) é um
componente que **um app já existente** passa a embutir — não um app novo que o Mabel gera do
zero. Até agora o repo tratava `Mabel.Host.Android` como um módulo Gradle comum, pensado para
ser incluído por caminho de fonte (`include(":Mabel.Host.Android")` num `settings.gradle.kts`
multi-módulo convencional) e `Mabel.Wasi.Protocol` como um projeto .NET referenciado por
`ProjectReference` dentro do próprio `Mabel.sln`.

Uma tentativa real de embutir `Mabel.Host.Android` num app Android **externo** ao monorepo
(um app anfitrião que já existe, com seu próprio build) expôs que essa suposição não se
sustenta quando o build do app anfitrião é um **build Gradle composto/incluído**
(`includeBuild`, um recurso padrão do Gradle — não algo específico deste repo) em vez de um
único build multi-módulo:

- **Compartilhar o source-set diretamente** (apontar o `sourceSets["main"]` do módulo
  anfitrião para o `src/main` de `Mabel.Host.Android`) esbarra num bug real de isolamento de
  classloader do Android Gradle Plugin: o tipo decorado de source-set carregado no classloader
  de um build é um tipo *diferente*, em runtime, do "mesmo" tipo carregado no classloader
  isolado do build incluído — o cast (`DefaultAndroidLibrarySourceSet_Decorated` →
  `AndroidLibrarySourceSet`) falha na configuração. Reproduzido com três variações de sintaxe
  diferentes; não é erro de digitação.
- **Referenciar o módulo por coordenada de projeto cruzando builds** (`project(":mabelHost")`)
  também não funciona quando o `settings.gradle.kts` do build composto é **gerado do zero a
  cada build** por alguma ferramenta upstream — não há um lugar estável para declarar um
  módulo extra ali.

Os dois problemas são comportamento genérico de builds Gradle compostos, não uma peculiaridade
de um app específico — qualquer app anfitrião que use um build gerado/composto (comum em
ferramentas de scaffolding multiplataforma) vai bater no mesmo obstáculo ao tentar embutir
`Mabel.Host.Android` por fonte.

## Decisão

**D1 — Android: publicar `Mabel.Host.Android` como AAR real via `maven-publish`, num
repositório Maven local (`build/repo`).** O app anfitrião consome via coordenada normal
(`implementation("com.mabel.host:sdui:1.0")`) apontando pro repositório de arquivo, exatamente
como consumiria qualquer outra dependência AAR de terceiros. Isso contorna o isolamento de
classloader por completo: a dependência cruza a fronteira de build como **artefato binário**
resolvido pelo mecanismo padrão do Gradle, não como fiação de projeto/source-set.

**D2 — .NET: `Mabel.Wasi.Protocol` marcado como pacote (`IsPackable`, `PackageId`,
`Description`, `RepositoryUrl`).** Mesma lógica do lado .NET: um app anfitrião que não vive no
mesmo `.sln` deve poder consumir o protocolo/descritor SDUI como um pacote NuGet versionado, em
vez de exigir `ProjectReference` para um projeto-fonte dentro do monorepo do Mabel.

Em ambos os casos a decisão é a mesma, espelhada nos dois hosts: **o ponto de consumo de
`Mabel.Host.*` é um artefato binário versionado, não uma referência de código-fonte** — o que
mantém, na camada de distribuição, a mesma promessa de "um contrato, hosts co-iguais" que o
ADR 0002 já estabelece na camada de ABI.

## Alternativas consideradas

- **Manter inclusão por fonte, documentar que builds compostos não são suportados:** mais
  simples, mas contradiz o próprio pitch do README (app anfitrião **já existente** pode ter
  qualquer forma de build). Rejeitada.
- **Git submodule / subtree do `Mabel.Host.Android` dentro do app anfitrião:** ainda é
  inclusão por fonte — sofre do mesmo bug de classloader quando o anfitrião usa build
  composto. Rejeitada.
- **Publicar só quando houver um pipeline de release real (CI, versionamento semântico):**
  mais correto a longo prazo, mas bloquearia validar o mecanismo agora. Adotado como pendência
  (ver abaixo), não como bloqueio desta decisão.

## Consequências

- (+) Qualquer app nativo existente pode adotar `Mabel.Host.Android`/`Mabel.Wasi.Protocol` do
  jeito que já adota qualquer outra dependência de terceiros — sem precisar que o build do
  consumidor seja single-module, multi-module ou composto/gerado.
- (+) Contorna um bug reproduzido de isolamento de classloader do Gradle que não tem correção
  limpa no nível de compartilhamento de fonte.
- (+) Simetria entre os dois hosts: a mesma decisão de distribuição (binário versionado, não
  fonte) vale tanto pro lado Android/Gradle quanto pro lado .NET/NuGet.
- (−) Um repositório Maven local em `build/repo` **não é** um feed de pacotes real (NuGet.org,
  Maven Central, GitHub Packages, ou um registry privado) — esta ADR resolve só o **formato**
  do empacotamento; o pipeline de publicação real fica pendente.
- (−) Versionamento hoje é manual e fixo (`version = "1.0"`) — precisa acompanhar o
  versionamento real do framework assim que ele existir.
- (−) Adiciona um passo `maven-publish` + `afterEvaluate` ao build Android que precisa ficar em
  sincronia sempre que a superfície pública do módulo mudar.

## Pendências (a confirmar)

1. Canal de distribuição real: continuar publicando num repositório de arquivo local, ou
   configurar um registry de verdade (GitHub Packages, Maven Central, NuGet.org) quando o
   framework tiver uma cadência de release?
2. Esquema de versão dos pacotes — atrelar à tag de release geral do Mabel, ou versionar cada
   pacote de forma independente?
3. Confirmar se `Mabel.Host.iOS` (Swift Package, consumido via SPM — que já é inclusão por
   fonte/pacote por natureza) precisa do mesmo tratamento, ou se o modelo de distribuição do
   SPM já resolve isso sem o bug de classloader equivalente (SPM não tem builds compostos no
   mesmo sentido do Gradle `includeBuild`).
