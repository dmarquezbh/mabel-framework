;; Módulo de teste v2 — MESMA forma do v1 (memória linear + increment/get/version),
;; mas deliberadamente DIFERENTE em bytes e comportamento:
;;   - increment() soma 10 em vez de 1 (prova que o host está rodando o
;;     código NOVO, não reusando o v1 por engano/cache).
;;   - version() retorna 2.
;; O ponto do spike: instanciar isto por cima de uma engine que já tinha o v1
;; rodando (com estado != 0) e confirmar que a memória linear nasce ZERADA —
;; não herda o contador do v1. Isso é a evidência empírica do que
;; docs/hmr-e-estado.md §1 já previu ("a memória linear nova nasce zerada").
(module
  (memory (export "memory") 1)

  (func $increment (export "increment") (result i32)
    (i32.store (i32.const 0)
      (i32.add (i32.load (i32.const 0)) (i32.const 10)))
    (i32.load (i32.const 0)))

  (func $get (export "get") (result i32)
    (i32.load (i32.const 0)))

  (func $version (export "version") (result i32)
    (i32.const 2))
)
