import SwiftUI

// =============================================================================
// Mabel Host - SwiftUI integration (SDUI)
// Wrapper SwiftUI que constrói a árvore de CONTROLES NATIVOS a partir de um
// SduiDocument (via MabelViewBuilder) e roteia o tap resolvido (ação + nó) pro
// app. Substitui o antigo wrapper de canvas (MabelCanvasView preservado só como
// referência do display-list — não usado neste caminho).
// =============================================================================

public struct MabelView: UIViewRepresentable {
    var document: SduiDocument?
    var onAction: ((SduiAction, SduiNode) -> Void)?

    public init(document: SduiDocument?,
                onAction: ((SduiAction, SduiNode) -> Void)? = nil) {
        self.document = document
        self.onAction = onAction
    }

    public func makeUIView(context: Context) -> UIView {
        let container = UIView()
        container.backgroundColor = .systemBackground
        return container
    }

    public func updateUIView(_ container: UIView, context: Context) {
        container.subviews.forEach { $0.removeFromSuperview() }
        guard let document else { return }
        let builder = MabelViewBuilder(onAction: onAction)
        let root = builder.build(document)
        root.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(root)
        NSLayoutConstraint.activate([
            root.topAnchor.constraint(equalTo: container.safeAreaLayoutGuide.topAnchor),
            root.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            root.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            root.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
    }
}
