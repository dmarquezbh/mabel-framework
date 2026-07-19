import SwiftUI

// =============================================================================
// HarnessView — lista de capabilities + log ao vivo. Toque = roda o teste
// chamando o CapabilityHost direto (prova a impl nativa). "⚠️device" = precisa
// de hardware real (câmera/BLE/biometria/haptics/GPS) pra validar de fato.
// =============================================================================

struct HarnessView: View {
    @StateObject private var model = HarnessModel()

    var body: some View {
        NavigationView {
            VStack(spacing: 0) {
                List(HarnessTest.allCases) { test in
                    Button(test.label) { model.run(test) }
                        .font(.system(.body, design: .rounded))
                }
                .frame(maxHeight: 340)

                Divider()

                ScrollView {
                    VStack(alignment: .leading, spacing: 2) {
                        ForEach(Array(model.log.enumerated()), id: \.offset) { _, line in
                            Text(line)
                                .font(.system(size: 11, design: .monospaced))
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                    .padding(8)
                }
                .background(Color.black.opacity(0.04))
            }
            .navigationTitle("Mabel Caps Harness")
        }
        .navigationViewStyle(.stack)
    }
}
