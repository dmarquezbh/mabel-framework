import UIKit

// =============================================================================
// Mabel SDUI — host iOS
// Decodifica um SduiDocument (árvore semântica emitida pelo guest) e instancia
// CONTROLES NATIVOS UIKit reais (UIScrollView/UIStackView/UILabel/UIControl/
// UIProgressView). Sem canvas, sem pixels — scroll, hit-testing, a11y e Dynamic
// Type vêm do SO. Espelha Mabel.Wasi.Protocol/Sdui/Descriptor.cs.
// =============================================================================

// MARK: - Modelo (Decodable, espelha o schema C#)

public enum SduiNodeType: UInt8, Decodable {
    case screen = 1, vstack = 2, hstack = 3, scrollView = 4, list = 5
    case card = 6, text = 7, button = 8, image = 9, badge = 10
    case progressBar = 11, divider = 12, spacer = 13
}

public struct SduiEdges: Decodable {
    public let top: CGFloat
    public let right: CGFloat
    public let bottom: CGFloat
    public let left: CGFloat
}

public struct SduiAction: Decodable {
    public let name: String
    public let args: [String: String]?
}

public struct SduiProps: Decodable {
    // Layout
    public let spacing: CGFloat?
    public let padding: SduiEdges?
    public let align: UInt8?
    public let width: CGFloat?
    public let height: CGFloat?
    public let flex: CGFloat?
    public let axis: UInt8?
    // Box
    public let background: UInt32?
    public let cornerRadius: CGFloat?
    public let borderColor: UInt32?
    public let borderWidth: CGFloat?
    // Text
    public let text: String?
    public let fontSize: CGFloat?
    public let color: UInt32?
    public let weight: UInt8?
    // Misc
    public let src: String?
    public let value: CGFloat?
    public let data: [String: String]?
}

public struct SduiNode: Decodable {
    public let id: String
    public let type: SduiNodeType
    public let props: SduiProps?
    public let children: [SduiNode]?
    public let onTap: SduiAction?
}

public struct SduiDocument: Decodable {
    public let schemaVersion: Int
    public let root: SduiNode
}

// MARK: - Helpers de estilo

/// RGBA 0xRRGGBBAA → UIColor (mesmo formato do RenderCommand).
func sduiColor(_ rgba: UInt32) -> UIColor {
    let r = CGFloat((rgba >> 24) & 0xFF) / 255.0
    let g = CGFloat((rgba >> 16) & 0xFF) / 255.0
    let b = CGFloat((rgba >> 8) & 0xFF) / 255.0
    let a = CGFloat(rgba & 0xFF) / 255.0
    return UIColor(red: r, green: g, blue: b, alpha: a)
}

func sduiFont(size: CGFloat?, weight: UInt8?) -> UIFont {
    let s = size ?? 14
    let w: UIFont.Weight
    switch weight ?? 0 {
    case 1: w = .medium
    case 2: w = .semibold
    case 3: w = .bold
    default: w = .regular
    }
    return .systemFont(ofSize: s, weight: w)
}

// MARK: - Controles auxiliares

/// UILabel com padding interno (usado em Badge/chip).
final class PaddingLabel: UILabel {
    var insets = UIEdgeInsets(top: 2, left: 6, bottom: 2, right: 6)
    override func drawText(in rect: CGRect) { super.drawText(in: rect.inset(by: insets)) }
    override var intrinsicContentSize: CGSize {
        let s = super.intrinsicContentSize
        return CGSize(width: s.width + insets.left + insets.right,
                      height: s.height + insets.top + insets.bottom)
    }
}

/// Card nativo clicável. Encapsula um stack vertical de filhos + tap→ação.
final class MabelCardControl: UIControl {
    let stack = UIStackView()
    var node: SduiNode?
    var onAction: ((SduiAction, SduiNode) -> Void)?

    override init(frame: CGRect) {
        super.init(frame: frame)
        stack.axis = .vertical
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.isUserInteractionEnabled = false // o UIControl recebe o tap
        addSubview(stack)
        addTarget(self, action: #selector(tapped), for: .touchUpInside)
    }
    required init?(coder: NSCoder) { super.init(coder: coder); fatalError() }

    @objc private func tapped() {
        guard let node, let action = node.onTap else { return }
        // Feedback breve de toque.
        UIView.animate(withDuration: 0.08, animations: { self.alpha = 0.6 }) { _ in
            UIView.animate(withDuration: 0.12) { self.alpha = 1 }
        }
        onAction?(action, node)
    }
}

// MARK: - Builder

/// Percorre a árvore SDUI e devolve a UIView raiz com controles nativos.
@MainActor
public final class MabelViewBuilder {
    /// Ação de tap resolvida: recebe {action, node.id, node.data}.
    public var onAction: ((SduiAction, SduiNode) -> Void)?

    public init(onAction: ((SduiAction, SduiNode) -> Void)? = nil) {
        self.onAction = onAction
    }

    public func build(_ doc: SduiDocument) -> UIView {
        return build(node: doc.root)
    }

    private func build(node: SduiNode) -> UIView {
        let view: UIView
        switch node.type {
        case .screen:      view = buildContainer(node)
        case .scrollView:  view = buildScroll(node)
        case .vstack:      view = buildStack(node, axis: .vertical)
        case .hstack:      view = buildStack(node, axis: .horizontal)
        case .list:        view = buildStack(node, axis: (node.props?.axis == 1) ? .horizontal : .vertical)
        case .card:        view = buildCard(node)
        case .text:        view = buildText(node)
        case .badge:       view = buildBadge(node)
        case .progressBar: view = buildProgress(node)
        case .button:      view = buildText(node)   // proof: botão vira label estilizado
        case .image:       view = buildContainer(node)
        case .divider:     view = buildDivider(node)
        case .spacer:      view = UIView()
        }
        applyBox(node.props, to: view)
        applySize(node.props, to: view)
        return view
    }

    // Screen / container genérico: empilha o(s) filho(s) e fixa nas bordas.
    private func buildContainer(_ node: SduiNode) -> UIView {
        let v = UIView()
        v.translatesAutoresizingMaskIntoConstraints = false
        if let child = node.children?.first {
            let c = build(node: child)
            c.translatesAutoresizingMaskIntoConstraints = false
            v.addSubview(c)
            NSLayoutConstraint.activate([
                c.topAnchor.constraint(equalTo: v.topAnchor),
                c.leadingAnchor.constraint(equalTo: v.leadingAnchor),
                c.trailingAnchor.constraint(equalTo: v.trailingAnchor),
                c.bottomAnchor.constraint(equalTo: v.bottomAnchor),
            ])
        }
        return v
    }

    private func buildScroll(_ node: SduiNode) -> UIScrollView {
        let scroll = UIScrollView()
        scroll.translatesAutoresizingMaskIntoConstraints = false
        scroll.showsHorizontalScrollIndicator = true
        scroll.showsVerticalScrollIndicator = true
        scroll.alwaysBounceHorizontal = (node.props?.axis == 1)
        scroll.alwaysBounceVertical = (node.props?.axis != 1)
        guard let child = node.children?.first else { return scroll }
        let content = build(node: child)
        content.translatesAutoresizingMaskIntoConstraints = false
        scroll.addSubview(content)
        let g = scroll.contentLayoutGuide
        let f = scroll.frameLayoutGuide
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: g.topAnchor),
            content.leadingAnchor.constraint(equalTo: g.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: g.trailingAnchor),
            content.bottomAnchor.constraint(equalTo: g.bottomAnchor),
        ])
        // Eixo transversal fixado ao frame (scroll só no eixo declarado).
        if node.props?.axis == 1 {
            content.heightAnchor.constraint(equalTo: f.heightAnchor).isActive = true
        } else {
            content.widthAnchor.constraint(equalTo: f.widthAnchor).isActive = true
        }
        return scroll
    }

    private func buildStack(_ node: SduiNode, axis: NSLayoutConstraint.Axis) -> UIStackView {
        let stack = UIStackView()
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.axis = axis
        stack.spacing = node.props?.spacing ?? 0
        stack.alignment = alignment(node.props?.align, axis: axis)
        stack.distribution = .fill
        applyPadding(node.props?.padding, to: stack)
        for childNode in node.children ?? [] {
            let child = build(node: childNode)
            stack.addArrangedSubview(child)
            // flex → cresce no eixo; senão hugging alto (tamanho do conteúdo).
            let hugging: Float = (childNode.props?.flex ?? 0) > 0 ? 1 : 750
            child.setContentHuggingPriority(UILayoutPriority(hugging),
                                            for: axis == .horizontal ? .horizontal : .vertical)
        }
        return stack
    }

    private func buildCard(_ node: SduiNode) -> UIView {
        let card = MabelCardControl()
        card.translatesAutoresizingMaskIntoConstraints = false
        card.node = node
        card.onAction = onAction
        card.stack.spacing = node.props?.spacing ?? 4
        let m = node.props?.padding
        card.stack.isLayoutMarginsRelativeArrangement = true
        card.stack.layoutMargins = UIEdgeInsets(top: m?.top ?? 8, left: m?.left ?? 12,
                                                bottom: m?.bottom ?? 8, right: m?.right ?? 12)
        NSLayoutConstraint.activate([
            card.stack.topAnchor.constraint(equalTo: card.topAnchor),
            card.stack.leadingAnchor.constraint(equalTo: card.leadingAnchor),
            card.stack.trailingAnchor.constraint(equalTo: card.trailingAnchor),
            card.stack.bottomAnchor.constraint(equalTo: card.bottomAnchor),
        ])
        for childNode in node.children ?? [] {
            card.stack.addArrangedSubview(build(node: childNode))
        }
        return card
    }

    private func buildText(_ node: SduiNode) -> UILabel {
        let label = UILabel()
        label.translatesAutoresizingMaskIntoConstraints = false
        label.text = node.props?.text
        label.font = sduiFont(size: node.props?.fontSize, weight: node.props?.weight)
        label.textColor = node.props.flatMap { $0.color.map(sduiColor) } ?? .label
        label.lineBreakMode = .byTruncatingTail
        return label
    }

    private func buildBadge(_ node: SduiNode) -> UIView {
        let label = PaddingLabel()
        label.translatesAutoresizingMaskIntoConstraints = false
        label.text = node.props?.text
        label.font = sduiFont(size: node.props?.fontSize ?? 10, weight: node.props?.weight)
        label.textColor = node.props.flatMap { $0.color.map(sduiColor) } ?? .label
        label.textAlignment = .center
        label.setContentHuggingPriority(.required, for: .horizontal)
        return label
    }

    private func buildProgress(_ node: SduiNode) -> UIProgressView {
        let bar = UIProgressView(progressViewStyle: .default)
        bar.translatesAutoresizingMaskIntoConstraints = false
        bar.progress = Float(node.props?.value ?? 0)
        if let c = node.props?.color { bar.progressTintColor = sduiColor(c) }
        bar.trackTintColor = UIColor(white: 0.9, alpha: 1)
        return bar
    }

    private func buildDivider(_ node: SduiNode) -> UIView {
        let v = UIView()
        v.translatesAutoresizingMaskIntoConstraints = false
        v.backgroundColor = node.props.flatMap { $0.background.map(sduiColor) } ?? UIColor(white: 0.9, alpha: 1)
        v.heightAnchor.constraint(equalToConstant: 1).isActive = true
        return v
    }

    // MARK: - Aplicadores de props

    private func applyBox(_ props: SduiProps?, to view: UIView) {
        guard let props else { return }
        if let bg = props.background, !(view is UIProgressView) { view.backgroundColor = sduiColor(bg) }
        if let r = props.cornerRadius { view.layer.cornerRadius = r; view.clipsToBounds = true }
        if let bw = props.borderWidth { view.layer.borderWidth = bw }
        if let bc = props.borderColor { view.layer.borderColor = sduiColor(bc).cgColor }
    }

    private func applySize(_ props: SduiProps?, to view: UIView) {
        guard let props else { return }
        if let w = props.width { view.widthAnchor.constraint(equalToConstant: w).isActive = true }
        if let h = props.height { view.heightAnchor.constraint(equalToConstant: h).isActive = true }
    }

    private func applyPadding(_ edges: SduiEdges?, to stack: UIStackView) {
        guard let e = edges else { return }
        stack.isLayoutMarginsRelativeArrangement = true
        stack.layoutMargins = UIEdgeInsets(top: e.top, left: e.left, bottom: e.bottom, right: e.right)
    }

    private func alignment(_ align: UInt8?, axis: NSLayoutConstraint.Axis) -> UIStackView.Alignment {
        switch align ?? 3 { // default Stretch → .fill
        case 0: return axis == .horizontal ? .top : .leading
        case 1: return .center
        case 2: return axis == .horizontal ? .bottom : .trailing
        default: return .fill
        }
    }
}
