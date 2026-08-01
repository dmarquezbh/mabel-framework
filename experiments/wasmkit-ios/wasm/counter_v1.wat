;; Módulo de teste v1 — contador mutável em memória linear (não em global,
;; pra ficar mais perto de "estado real de app": um i32 na posição 0 do heap).
;;
;; Exports:
;;   memory        — memória linear (1 página = 64KiB), pra o host ler o byte
;;                    direto se quiser (não usado neste spike, mas documenta o modelo).
;;   increment()    -> i32   soma 1 ao contador guardado em memória[0] e retorna o novo valor
;;   get()          -> i32   lê o contador sem alterar
;;   version()      -> i32   identifica qual build está rodando (1 aqui, 2 no v2)
(module
  (memory (export "memory") 1)

  (func $increment (export "increment") (result i32)
    (i32.store (i32.const 0)
      (i32.add (i32.load (i32.const 0)) (i32.const 1)))
    (i32.load (i32.const 0)))

  (func $get (export "get") (result i32)
    (i32.load (i32.const 0)))

  (func $version (export "version") (result i32)
    (i32.const 1))
)
