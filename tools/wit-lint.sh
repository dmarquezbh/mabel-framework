#!/usr/bin/env bash
# =============================================================================
# wit-lint.sh — lint estrutural dos contratos WIT do Mabel (Onda 🟢 / CI).
#
# Os .wit (mabel:sdui@0.3.0, mabel:capabilities) são o CONTRATO SEMÂNTICO
# platform-neutral. Hoje NÃO passam por wit-bindgen (transporte real = JSON), então
# validamos a HIGIENE ESTRUTURAL que qualquer parser exige, de forma que roda em
# qualquer runner sem toolchain wasm:
#   • arquivo não-vazio;
#   • declara `package <ns>:<name>@<semver>;`;
#   • chaves { } balanceadas;
#   • parênteses ( ) balanceados;
#   • sem caractere TAB (estilo WIT = espaços);
#   • sem espaço em branco no fim de linha.
#
# A validação COMPLETA de parse (wasm-tools component wit) roda como passo
# ADVISORY no CI enquanto o WIT não é confirmado bindgen-clean.
#
# Uso: tools/wit-lint.sh [dir_ou_arquivo ...]   (default: todos os .wit em src/)
# Sai !=0 se qualquer arquivo falhar.
# =============================================================================
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
targets=("$@")
if [ ${#targets[@]} -eq 0 ]; then
  targets=("$ROOT/src")
fi

# Coleta a lista de .wit dos alvos.
files=()
for t in "${targets[@]}"; do
  if [ -d "$t" ]; then
    while IFS= read -r f; do files+=("$f"); done < <(find "$t" -type f -name '*.wit' | sort)
  elif [ -f "$t" ]; then
    files+=("$t")
  fi
done

if [ ${#files[@]} -eq 0 ]; then
  echo "wit-lint: nenhum arquivo .wit encontrado em: ${targets[*]}" >&2
  exit 1
fi

fail=0
checked=0

for f in "${files[@]}"; do
  checked=$((checked + 1))
  errs=()

  # não-vazio
  if [ ! -s "$f" ]; then
    errs+=("arquivo vazio")
  fi

  # declaração de package
  if ! grep -Eq '^[[:space:]]*package[[:space:]]+[a-z0-9_-]+:[a-z0-9_-]+@[0-9]+\.[0-9]+\.[0-9]+[[:space:]]*;' "$f"; then
    errs+=("faltou declaração 'package ns:name@x.y.z;'")
  fi

  # chaves balanceadas
  opens=$(tr -cd '{' < "$f" | wc -c)
  closes=$(tr -cd '}' < "$f" | wc -c)
  if [ "$opens" -ne "$closes" ]; then
    errs+=("chaves desbalanceadas ({=$opens }=$closes)")
  fi

  # parênteses balanceados
  po=$(tr -cd '(' < "$f" | wc -c)
  pc=$(tr -cd ')' < "$f" | wc -c)
  if [ "$po" -ne "$pc" ]; then
    errs+=("parênteses desbalanceados ((=$po )=$pc)")
  fi

  # sem TAB
  if grep -Pq '\t' "$f" 2>/dev/null; then
    errs+=("contém TAB (use espaços)")
  fi

  # sem trailing whitespace
  if grep -nq '[[:space:]]$' "$f"; then
    errs+=("espaço em branco no fim de linha")
  fi

  rel="${f#"$ROOT"/}"
  if [ ${#errs[@]} -eq 0 ]; then
    echo "  ok    $rel"
  else
    fail=1
    for e in "${errs[@]}"; do
      echo "  FAIL  $rel: $e" >&2
    done
  fi
done

echo "wit-lint: $checked arquivo(s) verificado(s)."
if [ "$fail" -ne 0 ]; then
  echo "wit-lint: FALHOU." >&2
  exit 1
fi
echo "wit-lint: OK."
