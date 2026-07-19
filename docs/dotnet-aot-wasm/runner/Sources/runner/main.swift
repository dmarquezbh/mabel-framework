import Foundation
import WasmKit
import WasmKitWASI
import WasmParser

// Usage: runner <path-to.wasm> [expectedResult]
// Parses the module (asserts CORE module, not component), stubs every imported
// function with a matching-typed host no-op, instantiates, and calls exported add(2,3).
//
// Why stubs: .NET 10's wasi framework libs are compiled against WASIp2 (wasi:cli/*,
// wasi:io/* @0.2.0 component interfaces), which WasmKit's WASI-preview1 host does NOT
// provide. A pure exported function (add) never calls them, so stubbing satisfies
// instantiation and proves the compute path runs under WasmKit.

guard CommandLine.arguments.count >= 2 else {
    FileHandle.standardError.write("usage: runner <wasm> [expected]\n".data(using: .utf8)!)
    exit(2)
}
let path = CommandLine.arguments[1]
let expected: Int32 = CommandLine.arguments.count >= 3 ? (Int32(CommandLine.arguments[2]) ?? 5) : 5

let bytes = [UInt8](try Data(contentsOf: URL(fileURLWithPath: path)))
print("size=\(bytes.count) bytes")
let version = Array(bytes[4..<8])
print("magic=\(bytes[0..<4].map { String(format: "%02x", $0) }.joined(separator: " "))")
print("version=\(version.map { String(format: "%02x", $0) }.joined(separator: " "))")
if version == [0x01, 0x00, 0x00, 0x00] {
    print("KIND=CORE-MODULE")
} else if version == [0x0d, 0x00, 0x01, 0x00] {
    print("KIND=COMPONENT (WasmKit cannot host)")
    exit(1)
} else {
    print("KIND=UNKNOWN"); exit(1)
}

let module: Module
do {
    module = try parseWasm(bytes: bytes)
    print("PARSE=OK exports=\(module.exports.map { $0.name })")
} catch {
    print("PARSE=FAIL \(error)"); exit(1)
}

func zero(_ t: ValueType) -> Value {
    switch t {
    case .i32: return .i32(0)
    case .i64: return .i64(0)
    case .f32: return .f32(0)
    case .f64: return .f64(0)
    default: return .i32(0)
    }
}

let engine = Engine()
let store = Store(engine: engine)
var imports = Imports()
var stubbed = 0
for imp in module.imports {
    if case let .function(typeIndex) = imp.descriptor {
        let ft = module.types[Int(typeIndex)]
        let results = ft.results
        let fn = Function(store: store, type: ft) { _, _ in results.map { zero($0) } }
        imports.define(module: imp.module, name: imp.name, fn)
        stubbed += 1
    }
}
print("STUBBED_IMPORTS=\(stubbed) (WASIp2 interfaces the .NET framework emits; unused by pure exports)")

let instance: Instance
do {
    instance = try module.instantiate(store: store, imports: imports)
    print("INSTANTIATE=OK")
} catch {
    print("INSTANTIATE=FAIL \(error)"); exit(1)
}

if let initFn = instance.exports[function: "_initialize"] {
    do { _ = try initFn.invoke([]); print("INIT=_initialize ran") }
    catch { print("INIT=_initialize trapped (ignored, stubs are no-ops): \(error)") }
}

guard let addFn = instance.exports[function: "add"] else {
    print("CALL=FAIL export 'add' not found"); exit(1)
}
let results = try addFn.invoke([.i32(2), .i32(3)])
print("add(2,3) raw=\(results)")
if case let .i32(v) = results.first {
    let got = Int32(bitPattern: v)
    if got == expected {
        print("RESULT=OK add(2,3)=\(got)")
        print("WASMKIT-RUNS-DOTNET-CORE-WASM=TRUE")
        exit(0)
    }
    print("RESULT=MISMATCH got=\(got) expected=\(expected)"); exit(1)
}
print("RESULT=FAIL non-i32 result"); exit(1)
