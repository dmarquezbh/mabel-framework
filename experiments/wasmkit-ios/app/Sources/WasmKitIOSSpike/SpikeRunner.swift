import Foundation
import UIKit
import WasmKit

// =============================================================================
// SpikeRunner — a lógica central do spike, isolada de SwiftUI pra ficar fácil
// de ler/auditar. Ver README.md do experimento pra objetivo completo.
//
// O que este arquivo prova ou refuta, com dado real medido NO DEVICE:
//   1. WasmKit compila e RODA no iOS físico (não só macOS/Linux).
//   2. Dá pra instanciar counter_v1.wasm, chamar increment()/get() exportados,
//      e ver o estado (contador em memória linear) mudar de verdade.
//   3. Dá pra DESCARTAR essa instância e instanciar counter_v2.wasm no mesmo
//      processo host, sem reiniciar o app — e medir quanto tempo isso leva.
//   4. O que acontece com o estado no swap: a memória linear nova nasce
//      zerada (confirma ou refuta docs/hmr-e-estado.md §1) — v2 usa
//      increment()=+10 (em vez de +1) e version()=2, então dá pra distinguir
//      "zerou" de "carregou o v1 por engano/cache".
// =============================================================================

enum SpikeRunner {
    @MainActor
    static func run() -> String {
        var lines: [String] = []
        func log(_ s: String) { lines.append(s) }

        log("=== WasmKit-on-iOS spike ===")
        log("device: \(UIDevice.current.model) — iOS \(UIDevice.current.systemVersion)")
        log("processo: pid \(ProcessInfo.processInfo.processIdentifier)")
        log("")

        do {
            // ---------------------------------------------------------------
            // 0) Carrega os bytes dos dois módulos de teste do bundle.
            // ---------------------------------------------------------------
            let bytesV1 = try loadWasmResource(name: "counter_v1")
            let bytesV2 = try loadWasmResource(name: "counter_v2")
            log("counter_v1.wasm: \(bytesV1.count) bytes")
            log("counter_v2.wasm: \(bytesV2.count) bytes")
            log("")

            // ---------------------------------------------------------------
            // 1) Engine único pro processo inteiro (como um host real faria —
            //    o Engine é a peça "cara"/reusável; Store+Module+Instance são
            //    o que se troca no hot-swap).
            // ---------------------------------------------------------------
            let engine = Engine()

            // ---------------------------------------------------------------
            // 2) Carrega e instancia v1. Mede parse+instantiate separado.
            // ---------------------------------------------------------------
            let store1 = Store(engine: engine)
            let clock = ContinuousClock()

            let (module1, parseV1) = try measure(clock) { try parseWasm(bytes: bytesV1) }
            let (instance1, instantiateV1) = try measure(clock) {
                try module1.instantiate(store: store1)
            }
            log("[v1] parse:        \(fmt(parseV1))")
            log("[v1] instantiate:  \(fmt(instantiateV1))")
            log("[v1] load total:   \(fmt(parseV1 + instantiateV1))")

            guard let inc1 = instance1.exports[function: "increment"],
                  let get1 = instance1.exports[function: "get"],
                  let ver1 = instance1.exports[function: "version"]
            else {
                log("FALHA: exports increment/get/version não encontrados no v1")
                return lines.joined(separator: "\n")
            }

            var lastV1: Int32 = 0
            for _ in 1...5 {
                lastV1 = try i32(inc1.invoke([]))
            }
            let readBack1 = try i32(get1.invoke([]))
            let version1 = try i32(ver1.invoke([]))
            log("[v1] increment() x5 → contador = \(lastV1)")
            log("[v1] get()            → \(readBack1) (esperado 5)")
            log("[v1] version()        → \(version1) (esperado 1)")
            log("")

            // ---------------------------------------------------------------
            // 3) HOT-SWAP: descarta instance1/module1/store1 (deixa de
            //    referenciá-los — não há API explícita de "unload", é GC do
            //    ARC do Swift) e instancia v2 num Store NOVO, MESMO Engine,
            //    MESMO PROCESSO (sem reiniciar o app). Repete 5x pra ter
            //    média/min/max honestos (não só a 1ª amostra, que pode ter
            //    warmup de dyld/JIT do runtime WasmKit em si).
            // ---------------------------------------------------------------
            var swapTimes: [Duration] = []
            var lastInstance2: Instance?
            for i in 1...5 {
                let store = Store(engine: engine)
                let (module, parseT) = try measure(clock) { try parseWasm(bytes: bytesV2) }
                let (instance, instantiateT) = try measure(clock) {
                    try module.instantiate(store: store)
                }
                swapTimes.append(parseT + instantiateT)
                lastInstance2 = instance
                log("[v2] swap #\(i): parse \(fmt(parseT)) + instantiate \(fmt(instantiateT)) = \(fmt(parseT + instantiateT))")
            }
            guard let instance2 = lastInstance2 else {
                log("FALHA: instance2 nil"); return lines.joined(separator: "\n")
            }
            log("")
            log("[swap] min:  \(fmt(swapTimes.min()!))")
            log("[swap] max:  \(fmt(swapTimes.max()!))")
            log("[swap] avg:  \(fmt(average(swapTimes)))")
            log("")

            // ---------------------------------------------------------------
            // 4) O TESTE CENTRAL: o estado sobreviveu ao swap, ou zerou?
            //    lastV1 == 5 (contador do v1 antes do swap). Se v2.get()==0,
            //    confirma que a memória linear nova nasce zerada (previsto em
            //    docs/hmr-e-estado.md §1). Se v2.get()==5, o estado
            //    "vazou" entre instâncias (não esperado — seria um achado
            //    novo, não previsto pela doc, digno de investigar).
            // ---------------------------------------------------------------
            guard let get2 = instance2.exports[function: "get"],
                  let inc2 = instance2.exports[function: "increment"],
                  let ver2 = instance2.exports[function: "version"]
            else {
                log("FALHA: exports increment/get/version não encontrados no v2")
                return lines.joined(separator: "\n")
            }

            let stateRightAfterSwap = try i32(get2.invoke([]))
            log("[v2] get() logo após o swap → \(stateRightAfterSwap)")
            if stateRightAfterSwap == 0 {
                log("     ✅ ESTADO ZEROU no swap — confirma docs/hmr-e-estado.md §1")
                log("        (\"a memória linear nova nasce zerada\"). Preservar estado")
                log("        através do swap EXIGE um mecanismo explícito (serialize/")
                log("        restore, ADR 0003 opção b/c) — não é automático.")
            } else if stateRightAfterSwap == lastV1 {
                log("     ⚠️ ACHADO INESPERADO: estado do v1 (\(lastV1)) sobreviveu no v2.")
                log("        Isso NÃO era esperado pela doc — merece investigação")
                log("        (possível reuse de memória física pelo alocador, não")
                log("        герdado logicamente pelo módulo).")
            } else {
                log("     ⚠️ estado = \(stateRightAfterSwap), nem 0 nem \(lastV1) — investigar.")
            }
            log("")

            let afterIncV2 = try i32(inc2.invoke([]))
            let version2 = try i32(ver2.invoke([]))
            log("[v2] increment() 1x   → \(afterIncV2) (esperado 10, pois v2 soma +10)")
            log("[v2] version()        → \(version2) (esperado 2)")
            if afterIncV2 == 10 && version2 == 2 {
                log("     ✅ confirma que o código NOVO (v2) está de fato rodando —")
                log("        não é cache/reuso do v1 (comportamento +10 é exclusivo do v2).")
            } else {
                log("     ⚠️ comportamento do v2 não bate com o esperado — investigar.")
            }
            log("")
            log("=== FIM DO SPIKE — sem crash, processo host seguiu vivo o tempo todo ===")

        } catch {
            log("EXCEÇÃO durante o spike: \(error)")
        }

        return lines.joined(separator: "\n")
    }

    // MARK: - Helpers

    private static func i32(_ values: [Value]) throws -> Int32 {
        guard case let .i32(v) = values.first else {
            throw SpikeError.unexpectedResult
        }
        return Int32(bitPattern: v)
    }

    private static func measure<T>(_ clock: ContinuousClock, _ body: () throws -> T) throws -> (T, Duration) {
        let start = clock.now
        let value = try body()
        let elapsed = clock.now - start
        return (value, elapsed)
    }

    private static func average(_ ds: [Duration]) -> Duration {
        let totalNs = ds.reduce(0.0) { $0 + $1.nanosecondsDouble }
        return .nanoseconds(Int64(totalNs / Double(ds.count)))
    }

    private static func fmt(_ d: Duration) -> String {
        let ms = d.nanosecondsDouble / 1_000_000.0
        return String(format: "%.3f ms", ms)
    }

    private static func loadWasmResource(name: String) throws -> [UInt8] {
        let candidates: [URL?] = [
            Bundle.module.url(forResource: name, withExtension: "wasm", subdirectory: "Resources"),
            Bundle.module.url(forResource: name, withExtension: "wasm"),
        ]
        for case let url? in candidates {
            if let data = try? Data(contentsOf: url) {
                return [UInt8](data)
            }
        }
        // Fallback: busca recursiva no bundle inteiro (defensivo — layout de
        // resource bundle do SwiftPM já mudou entre versões no passado).
        if let resourcePath = Bundle.module.resourcePath,
           let enumerator = FileManager.default.enumerator(atPath: resourcePath) {
            for case let file as String in enumerator {
                if file.hasSuffix("\(name).wasm") {
                    let url = URL(fileURLWithPath: resourcePath).appendingPathComponent(file)
                    return [UInt8](try Data(contentsOf: url))
                }
            }
        }
        throw SpikeError.resourceNotFound(name)
    }

    enum SpikeError: Error {
        case resourceNotFound(String)
        case unexpectedResult
    }
}

private extension Duration {
    var nanosecondsDouble: Double {
        let (seconds, attoseconds) = self.components
        return Double(seconds) * 1_000_000_000.0 + Double(attoseconds) / 1_000_000_000.0
    }
}
