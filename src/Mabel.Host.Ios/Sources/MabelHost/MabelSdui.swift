import UIKit

// =============================================================================
// Mabel SDUI — host iOS (schema v2)
// Decodifica um SduiDocument (árvore semântica) e instancia CONTROLES NATIVOS
// UIKit reais. Onda 2: decode TOLERANTE (OTA-safe), Fallback, acessibilidade,
// layout responsivo (size-class + min/max/aspect/safeArea), listas virtualizadas
// (UICollectionView diffable) e navegação (UINavigationController).
// Espelha Mabel.Wasi.Protocol/Sdui/*.cs.
// =============================================================================

let kHostSchemaVersion = 3

// MARK: - Modelo (Decodable, espelha o schema C#; byte-enums como raw UInt8)

public enum SduiNodeType: UInt8 {
    case screen = 1, vstack = 2, hstack = 3, scrollView = 4, list = 5
    case card = 6, text = 7, button = 8, image = 9, badge = 10
    case progressBar = 11, divider = 12, spacer = 13, navStack = 14
    case textField = 15
}

public struct SduiEdges: Decodable {
    public let top: CGFloat
    public let right: CGFloat
    public let bottom: CGFloat
    public let left: CGFloat
}

public struct SduiNavigate: Decodable {
    public let kind: UInt8            // 0 push,1 pop,2 replace,3 root,4 popTo
    public let route: String?
    public let params: [String: String]?
}

public struct SduiAction: Decodable {
    public let name: String
    public let args: [String: String]?
    public let navigate: SduiNavigate?
}

public struct SduiA11y: Decodable {
    public let label: String?
    public let role: UInt8?          // SduiA11yRole
    public let hint: String?
    public let hidden: Bool?
    public let value: String?
    public let traits: UInt32?       // SduiA11yTraits flags
}

public struct SduiNav: Decodable {
    public let route: String?
    public let title: String?
    public let modal: Bool?
    public let hidesNavBar: Bool?
}

/// Props: campos `var` p/ permitir merge raso (responsivo). Byte-enums = raw UInt8.
public struct SduiProps: Decodable {
    // Layout
    public var spacing: CGFloat?
    public var padding: SduiEdges?
    public var align: UInt8?
    public var width: CGFloat?
    public var height: CGFloat?
    public var flex: CGFloat?
    public var axis: UInt8?
    // Box
    public var background: UInt32?
    public var cornerRadius: CGFloat?
    public var borderColor: UInt32?
    public var borderWidth: CGFloat?
    // Text
    public var text: String?
    public var fontSize: CGFloat?
    public var color: UInt32?
    public var weight: UInt8?
    // Misc
    public var src: String?
    public var value: CGFloat?
    // Layout responsivo / flex refinado
    public var minWidth: CGFloat?
    public var maxWidth: CGFloat?
    public var minHeight: CGFloat?
    public var maxHeight: CGFloat?
    public var aspectRatio: CGFloat?
    public var flexGrow: CGFloat?
    public var flexShrink: CGFloat?
    public var flexBasis: CGFloat?
    public var wrap: UInt8?
    public var safeArea: UInt8?      // SduiSafeArea flags
    public var data: [String: String]?
    // TextField
    public var placeholder: String?

    /// Merge RASO: campos setados em `o` vencem; os demais herdam de self.
    func merged(over o: SduiProps) -> SduiProps {
        var r = self
        if let v = o.spacing { r.spacing = v }
        if let v = o.padding { r.padding = v }
        if let v = o.align { r.align = v }
        if let v = o.width { r.width = v }
        if let v = o.height { r.height = v }
        if let v = o.flex { r.flex = v }
        if let v = o.axis { r.axis = v }
        if let v = o.background { r.background = v }
        if let v = o.cornerRadius { r.cornerRadius = v }
        if let v = o.borderColor { r.borderColor = v }
        if let v = o.borderWidth { r.borderWidth = v }
        if let v = o.text { r.text = v }
        if let v = o.fontSize { r.fontSize = v }
        if let v = o.color { r.color = v }
        if let v = o.weight { r.weight = v }
        if let v = o.src { r.src = v }
        if let v = o.value { r.value = v }
        if let v = o.minWidth { r.minWidth = v }
        if let v = o.maxWidth { r.maxWidth = v }
        if let v = o.minHeight { r.minHeight = v }
        if let v = o.maxHeight { r.maxHeight = v }
        if let v = o.aspectRatio { r.aspectRatio = v }
        if let v = o.flexGrow { r.flexGrow = v }
        if let v = o.flexShrink { r.flexShrink = v }
        if let v = o.flexBasis { r.flexBasis = v }
        if let v = o.wrap { r.wrap = v }
        if let v = o.safeArea { r.safeArea = v }
        if let v = o.placeholder { r.placeholder = v }
        return r
    }
}

public struct SduiResponsiveOverride: Decodable {
    public let widthClass: UInt8?    // SduiSizeClass 0 any,1 compact,2 regular
    public let heightClass: UInt8?
    public let minContainerWidth: CGFloat?
    public let props: SduiProps
}

public struct SduiListItem: Decodable {
    public let id: String
    public let data: [String: String]?
    public let onTap: SduiAction?
}

public final class SduiListData: Decodable {   // class: quebra o ciclo de valor SduiNode↔SduiListData
    public let itemTemplate: SduiNode
    public let items: [SduiListItem]?
    public let virtualized: Bool?
    public let axis: UInt8?
    public let estimatedItemExtent: CGFloat?
    public let count: Int?
    public let windowStart: Int?
}

/// Nó com decode TOLERANTE: `type` vem como raw UInt8; valor desconhecido não
/// estoura o parse — vira `typeRaw` sem `type` mapeado, e o builder aplica Fallback.
public struct SduiNode: Decodable {
    public let id: String
    public let typeRaw: UInt8
    public let props: SduiProps?
    public let children: [SduiNode]?
    public let onTap: SduiAction?
    public let a11y: SduiA11y?
    public let fallback: UInt8?          // 0 renderChildren,1 placeholder,2 ignore
    public let minSchemaVersion: Int?
    public let responsive: [SduiResponsiveOverride]?
    public let list: SduiListData?
    public let nav: SduiNav?
    public let bind: [String: String]?
    /// Ação de mudança de texto (TextField). Ver doc em Mabel.Wasi.Protocol/Sdui/Descriptor.cs.
    public let onChange: SduiAction?

    /// Tipo mapeado, ou nil se o host não conhece o valor (schema futuro).
    public var type: SduiNodeType? { SduiNodeType(rawValue: typeRaw) }

    enum CodingKeys: String, CodingKey {
        case id, type, props, children, onTap, a11y, fallback
        case minSchemaVersion, responsive, list, nav, bind, onChange
    }
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        typeRaw = try c.decode(UInt8.self, forKey: .type)   // raw → nunca estoura por valor desconhecido
        props = try c.decodeIfPresent(SduiProps.self, forKey: .props)
        children = try c.decodeIfPresent([SduiNode].self, forKey: .children)
        onTap = try c.decodeIfPresent(SduiAction.self, forKey: .onTap)
        a11y = try c.decodeIfPresent(SduiA11y.self, forKey: .a11y)
        fallback = try c.decodeIfPresent(UInt8.self, forKey: .fallback)
        minSchemaVersion = try c.decodeIfPresent(Int.self, forKey: .minSchemaVersion)
        responsive = try c.decodeIfPresent([SduiResponsiveOverride].self, forKey: .responsive)
        list = try c.decodeIfPresent(SduiListData.self, forKey: .list)
        nav = try c.decodeIfPresent(SduiNav.self, forKey: .nav)
        bind = try c.decodeIfPresent([String: String].self, forKey: .bind)
        onChange = try c.decodeIfPresent(SduiAction.self, forKey: .onChange)
    }
}

public struct SduiDocument: Decodable {
    public let schemaVersion: Int
    public let root: SduiNode
}

// MARK: - Helpers de estilo

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

final class PaddingLabel: UILabel {
    var insets = UIEdgeInsets(top: 2, left: 6, bottom: 2, right: 6)
    override func drawText(in rect: CGRect) { super.drawText(in: rect.inset(by: insets)) }
    override var intrinsicContentSize: CGSize {
        let s = super.intrinsicContentSize
        return CGSize(width: s.width + insets.left + insets.right,
                      height: s.height + insets.top + insets.bottom)
    }
}

final class MabelCardControl: UIControl {
    let stack = UIStackView()
    var node: SduiNode?
    var handler: ((SduiAction, SduiNode) -> Void)?

    override init(frame: CGRect) {
        super.init(frame: frame)
        stack.axis = .vertical
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.isUserInteractionEnabled = false
        addSubview(stack)
        addTarget(self, action: #selector(tapped), for: .touchUpInside)
    }
    required init?(coder: NSCoder) { super.init(coder: coder); fatalError() }

    @objc private func tapped() {
        guard let node, let action = node.onTap else { return }
        UIView.animate(withDuration: 0.08, animations: { self.alpha = 0.6 }) { _ in
            UIView.animate(withDuration: 0.12) { self.alpha = 1 }
        }
        handler?(action, node)
    }
}

/// Campo de texto nativo (TextField). O host é dono do texto corrente — o
/// servidor nunca o recebe de volta via Props, só via ação (onChange/onTap
/// com Args["text"] mesclado). Ver doc em Mabel.Wasi.Protocol/Sdui/Descriptor.cs.
final class MabelTextFieldControl: UITextField, UITextFieldDelegate {
    var node: SduiNode?
    var handler: ((SduiAction, SduiNode) -> Void)?

    override init(frame: CGRect) {
        super.init(frame: frame)
        delegate = self
        addTarget(self, action: #selector(changed), for: .editingChanged)
    }
    required init?(coder: NSCoder) { super.init(coder: coder); fatalError() }

    private func comTexto(_ base: SduiAction?) -> SduiAction? {
        guard let base else { return nil }
        var args = base.args ?? [:]
        args["text"] = text ?? ""
        return SduiAction(name: base.name, args: args, navigate: base.navigate)
    }

    @objc private func changed() {
        guard let node, let action = comTexto(node.onChange) else { return }
        handler?(action, node)
    }

    func textFieldShouldReturn(_ textField: UITextField) -> Bool {
        guard let node, let action = comTexto(node.onTap) else { return true }
        handler?(action, node)
        textField.resignFirstResponder()
        return true
    }
}

/// Container que RETÉM um UINavigationController e expõe sua view (NavStack).
final class NavHostView: UIView {
    let nav: UINavigationController
    init(nav: UINavigationController) {
        self.nav = nav
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        nav.view.translatesAutoresizingMaskIntoConstraints = false
        addSubview(nav.view)
        NSLayoutConstraint.activate([
            nav.view.topAnchor.constraint(equalTo: topAnchor),
            nav.view.leadingAnchor.constraint(equalTo: leadingAnchor),
            nav.view.trailingAnchor.constraint(equalTo: trailingAnchor),
            nav.view.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }
    required init?(coder: NSCoder) { fatalError() }
}

// MARK: - Builder

@MainActor
public final class MabelViewBuilder {
    public var onAction: ((SduiAction, SduiNode) -> Void)?

    // Estado de navegação (NavStack ativo + rotas → Screen).
    private weak var activeNav: UINavigationController?
    private var routes: [String: SduiNode] = [:]
    // Contexto de binding da linha atual (List virtualizada).
    private var bindingContext: [String: String]?

    public init(onAction: ((SduiAction, SduiNode) -> Void)? = nil) {
        self.onAction = onAction
    }

    public func build(_ doc: SduiDocument) -> UIView {
        return build(node: doc.root)
    }

    // Ação central: executa navegação declarativa (se houver) e encaminha ao app.
    private func handleAction(_ action: SduiAction, _ node: SduiNode) {
        if let navg = action.navigate { performNavigate(navg) }
        onAction?(action, node)
    }

    private func build(node: SduiNode) -> UIView {
        // 1) Degradação graciosa (OTA): tipo desconhecido ou schema mais novo.
        let needsFallback = node.type == nil
            || (node.minSchemaVersion.map { $0 > kHostSchemaVersion } ?? false)
        if needsFallback {
            return buildFallback(node)
        }

        // 2) Props resolvidas (responsivo: merge da 1ª variação que casa).
        let props = resolveProps(node)

        // 3) Lista virtualizada tem precedência sobre children estáticos.
        let view: UIView
        if node.type == .list, let listData = node.list {
            view = buildVirtualizedList(node, listData, props)
        } else {
            switch node.type! {
            case .screen:      view = buildContainer(node, props)
            case .scrollView:  view = buildScroll(node, props)
            case .vstack:      view = buildStack(node, props, axis: .vertical)
            case .hstack:      view = buildStack(node, props, axis: .horizontal)
            case .list:        view = buildStack(node, props, axis: (props?.axis == 1) ? .horizontal : .vertical)
            case .card:        view = buildCard(node, props)
            case .text:        view = buildText(node, props)
            case .badge:       view = buildBadge(node, props)
            case .progressBar: view = buildProgress(node, props)
            case .button:      view = buildButton(node, props)
            case .image:       view = buildImage(node, props)
            case .divider:     view = buildDivider(node, props)
            case .spacer:      view = UIView()
            case .navStack:    view = buildNavStack(node, props)
            case .textField:   view = buildTextField(node, props)
            }
        }
        applyBox(props, to: view)
        applySize(props, to: view)
        applyA11y(node.a11y, to: view)
        return view
    }

    // MARK: - Fallback (degradação graciosa)

    private func buildFallback(_ node: SduiNode) -> UIView {
        NSLog("[Kanban] fallback node=\(node.id) typeRaw=\(node.typeRaw) policy=\(node.fallback ?? 0)")
        switch node.fallback ?? 0 {   // default RenderChildren
        case 2: // Ignore — não ocupa espaço
            let v = UIView()
            v.translatesAutoresizingMaskIntoConstraints = false
            v.isHidden = true
            return v
        case 1: // Placeholder visível
            let l = PaddingLabel()
            l.translatesAutoresizingMaskIntoConstraints = false
            l.text = "⚠︎ nó não suportado (\(node.typeRaw))"
            l.font = .systemFont(ofSize: 11)
            l.textColor = .secondaryLabel
            l.backgroundColor = UIColor.systemYellow.withAlphaComponent(0.25)
            l.layer.cornerRadius = 4
            l.clipsToBounds = true
            return l
        default: // RenderChildren — container transparente com os filhos
            let stack = UIStackView()
            stack.translatesAutoresizingMaskIntoConstraints = false
            stack.axis = .vertical
            stack.spacing = node.props?.spacing ?? 0
            for c in node.children ?? [] { stack.addArrangedSubview(build(node: c)) }
            return stack
        }
    }

    // MARK: - Responsivo

    private func resolveProps(_ node: SduiNode) -> SduiProps? {
        guard let overrides = node.responsive, !overrides.isEmpty else { return node.props }
        let tc = UIScreen.main.traitCollection
        let containerW = UIScreen.main.bounds.width
        for o in overrides where matches(o, tc: tc, containerW: containerW) {
            let base = node.props ?? o.props
            return base.merged(over: o.props)
        }
        return node.props
    }

    private func matches(_ o: SduiResponsiveOverride, tc: UITraitCollection, containerW: CGFloat) -> Bool {
        if let wc = o.widthClass, wc != 0, sizeClass(tc.horizontalSizeClass) != wc { return false }
        if let hc = o.heightClass, hc != 0, sizeClass(tc.verticalSizeClass) != hc { return false }
        if let minW = o.minContainerWidth, containerW < minW { return false }
        return true
    }

    private func sizeClass(_ c: UIUserInterfaceSizeClass) -> UInt8 {
        switch c { case .compact: return 1; case .regular: return 2; default: return 0 }
    }

    // MARK: - Navegação

    private func buildNavStack(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let nav = UINavigationController()
        activeNav = nav
        // Indexa rotas dos Screens filhos e usa o 1º como raiz.
        var rootVC: UIViewController?
        for child in node.children ?? [] where child.type == .screen {
            if let route = child.nav?.route { routes[route] = child }
            if rootVC == nil { rootVC = screenVC(child) }
        }
        nav.viewControllers = [rootVC ?? UIViewController()]
        return NavHostView(nav: nav)
    }

    /// Empacota um Screen num UIViewController (título/nav bar do SduiNav).
    private func screenVC(_ screen: SduiNode) -> UIViewController {
        let vc = UIViewController()
        vc.title = screen.nav?.title
        if screen.nav?.hidesNavBar == true { vc.navigationItem.setHidesBackButton(true, animated: false) }
        let content = build(node: screen)
        content.translatesAutoresizingMaskIntoConstraints = false
        vc.view.backgroundColor = .systemBackground
        vc.view.addSubview(content)
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: vc.view.safeAreaLayoutGuide.topAnchor),
            content.leadingAnchor.constraint(equalTo: vc.view.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: vc.view.trailingAnchor),
            content.bottomAnchor.constraint(equalTo: vc.view.bottomAnchor),
        ])
        return vc
    }

    private func performNavigate(_ n: SduiNavigate) {
        NSLog("[Kanban] navigate kind=\(n.kind) route=\(n.route ?? "-") activeNav=\(activeNav != nil)")
        guard let nav = activeNav else { return }
        switch n.kind {
        case 0: // push
            if let route = n.route, let screen = routes[route] {
                nav.pushViewController(screenVC(screen), animated: true)
            }
        case 1: // pop
            nav.popViewController(animated: true)
        case 2: // replace
            if let route = n.route, let screen = routes[route] {
                var vcs = nav.viewControllers
                if !vcs.isEmpty { vcs[vcs.count - 1] = screenVC(screen) }
                nav.setViewControllers(vcs, animated: true)
            }
        case 3: // root
            if let route = n.route, let screen = routes[route] {
                nav.setViewControllers([screenVC(screen)], animated: true)
            } else if let first = nav.viewControllers.first {
                nav.setViewControllers([first], animated: true)
            }
        case 4: // popTo
            if let route = n.route, let target = nav.viewControllers.first(where: { $0.title == routes[route]?.nav?.title }) {
                nav.popToViewController(target, animated: true)
            }
        default: break
        }
    }

    // MARK: - Construtores de nó

    private func buildContainer(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let v = UIView()
        v.translatesAutoresizingMaskIntoConstraints = false
        if let child = node.children?.first {
            let c = build(node: child)
            c.translatesAutoresizingMaskIntoConstraints = false
            v.addSubview(c)
            let respectsSafe = (props?.safeArea ?? 0) != 0
            let topAnchorRef = respectsSafe ? v.safeAreaLayoutGuide.topAnchor : v.topAnchor
            let bottomAnchorRef = respectsSafe ? v.safeAreaLayoutGuide.bottomAnchor : v.bottomAnchor
            NSLayoutConstraint.activate([
                c.topAnchor.constraint(equalTo: topAnchorRef),
                c.leadingAnchor.constraint(equalTo: v.leadingAnchor),
                c.trailingAnchor.constraint(equalTo: v.trailingAnchor),
                c.bottomAnchor.constraint(equalTo: bottomAnchorRef),
            ])
        }
        return v
    }

    private func buildScroll(_ node: SduiNode, _ props: SduiProps?) -> UIScrollView {
        let scroll = UIScrollView()
        scroll.translatesAutoresizingMaskIntoConstraints = false
        scroll.alwaysBounceHorizontal = (props?.axis == 1)
        scroll.alwaysBounceVertical = (props?.axis != 1)
        guard let child = node.children?.first else { return scroll }
        let content = build(node: child)
        content.translatesAutoresizingMaskIntoConstraints = false
        scroll.addSubview(content)
        let g = scroll.contentLayoutGuide, f = scroll.frameLayoutGuide
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: g.topAnchor),
            content.leadingAnchor.constraint(equalTo: g.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: g.trailingAnchor),
            content.bottomAnchor.constraint(equalTo: g.bottomAnchor),
        ])
        if props?.axis == 1 {
            content.heightAnchor.constraint(equalTo: f.heightAnchor).isActive = true
        } else {
            content.widthAnchor.constraint(equalTo: f.widthAnchor).isActive = true
        }
        return scroll
    }

    private func buildStack(_ node: SduiNode, _ props: SduiProps?, axis: NSLayoutConstraint.Axis) -> UIStackView {
        let stack = UIStackView()
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.axis = axis
        stack.spacing = props?.spacing ?? 0
        stack.alignment = alignment(props?.align, axis: axis)
        stack.distribution = .fill
        applyPadding(props?.padding, to: stack)
        for childNode in node.children ?? [] {
            let child = build(node: childNode)
            stack.addArrangedSubview(child)
            // Uma List virtualizada (UICollectionView) não tem altura intrínseca:
            // num stack .fill sem flex ela colapsaria a 0. Trata como flexível por
            // padrão pra preencher o espaço restante.
            let isFillingList = childNode.type == .list && childNode.list != nil
            let grow = childNode.props?.flexGrow ?? childNode.props?.flex ?? (isFillingList ? 1 : 0)
            let hugging: Float = grow > 0 ? 1 : 750
            child.setContentHuggingPriority(UILayoutPriority(hugging),
                                            for: axis == .horizontal ? .horizontal : .vertical)
            child.setContentCompressionResistancePriority(grow > 0 ? .defaultLow : .required,
                                                           for: axis == .horizontal ? .horizontal : .vertical)
        }
        return stack
    }

    private func buildCard(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let card = MabelCardControl()
        card.translatesAutoresizingMaskIntoConstraints = false
        card.node = node
        card.handler = { [weak self] a, n in self?.handleAction(a, n) }
        card.stack.spacing = props?.spacing ?? 4
        let m = props?.padding
        card.stack.isLayoutMarginsRelativeArrangement = true
        card.stack.layoutMargins = UIEdgeInsets(top: m?.top ?? 8, left: m?.left ?? 12,
                                                bottom: m?.bottom ?? 8, right: m?.right ?? 12)
        NSLayoutConstraint.activate([
            card.stack.topAnchor.constraint(equalTo: card.topAnchor),
            card.stack.leadingAnchor.constraint(equalTo: card.leadingAnchor),
            card.stack.trailingAnchor.constraint(equalTo: card.trailingAnchor),
            card.stack.bottomAnchor.constraint(equalTo: card.bottomAnchor),
        ])
        for childNode in node.children ?? [] { card.stack.addArrangedSubview(build(node: childNode)) }
        return card
    }

    /// Resolve texto: se o nó tem Bind["text"] e há contexto de linha, usa o dado.
    private func boundText(_ node: SduiNode, _ props: SduiProps?) -> String? {
        if let key = node.bind?["text"], let v = bindingContext?[key] { return v }
        return props?.text
    }

    private func buildText(_ node: SduiNode, _ props: SduiProps?) -> UILabel {
        let label = UILabel()
        label.translatesAutoresizingMaskIntoConstraints = false
        label.text = boundText(node, props)
        label.font = sduiFont(size: props?.fontSize, weight: props?.weight)
        label.textColor = props?.color.map(sduiColor) ?? .label
        label.numberOfLines = 1
        label.lineBreakMode = .byTruncatingTail
        label.adjustsFontForContentSizeCategory = true
        return label
    }

    private func buildBadge(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let label = PaddingLabel()
        label.translatesAutoresizingMaskIntoConstraints = false
        label.text = boundText(node, props)
        label.font = sduiFont(size: props?.fontSize ?? 10, weight: props?.weight)
        label.textColor = props?.color.map(sduiColor) ?? .label
        label.textAlignment = .center
        label.setContentHuggingPriority(.required, for: .horizontal)
        return label
    }

    private func buildButton(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        var cfg = UIButton.Configuration.plain()
        cfg.title = boundText(node, props)
        let button = UIButton(configuration: cfg)
        button.translatesAutoresizingMaskIntoConstraints = false
        if let c = props?.color { button.setTitleColor(sduiColor(c), for: .normal) }
        button.addAction(UIAction { [weak self] _ in
            guard let self, let action = node.onTap else { return }
            self.handleAction(action, node)
        }, for: .touchUpInside)
        return button
    }

    private func buildTextField(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let field = MabelTextFieldControl()
        field.translatesAutoresizingMaskIntoConstraints = false
        field.node = node
        field.handler = { [weak self] a, n in self?.handleAction(a, n) }
        field.text = props?.text
        field.placeholder = props?.placeholder
        field.font = sduiFont(size: props?.fontSize, weight: props?.weight)
        if let c = props?.color { field.textColor = sduiColor(c) }
        field.returnKeyType = .send
        field.borderStyle = .roundedRect
        return field
    }

    private func buildImage(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let iv = UIImageView()
        iv.translatesAutoresizingMaskIntoConstraints = false
        iv.contentMode = .scaleAspectFit
        if let name = props?.src { iv.image = UIImage(systemName: name) ?? UIImage(named: name) }
        if let c = props?.color { iv.tintColor = sduiColor(c) }
        return iv
    }

    private func buildProgress(_ node: SduiNode, _ props: SduiProps?) -> UIProgressView {
        let bar = UIProgressView(progressViewStyle: .default)
        bar.translatesAutoresizingMaskIntoConstraints = false
        var v = props?.value ?? 0
        if let key = node.bind?["value"], let s = bindingContext?[key], let d = Double(s) { v = CGFloat(d) }
        bar.progress = Float(v)
        if let c = props?.color { bar.progressTintColor = sduiColor(c) }
        bar.trackTintColor = UIColor(white: 0.9, alpha: 1)
        return bar
    }

    private func buildDivider(_ node: SduiNode, _ props: SduiProps?) -> UIView {
        let v = UIView()
        v.translatesAutoresizingMaskIntoConstraints = false
        v.backgroundColor = props?.background.map(sduiColor) ?? UIColor(white: 0.9, alpha: 1)
        v.heightAnchor.constraint(equalToConstant: 1).isActive = true
        return v
    }

    // MARK: - Lista virtualizada (UICollectionView diffable)

    private func buildVirtualizedList(_ node: SduiNode, _ data: SduiListData, _ props: SduiProps?) -> UIView {
        NSLog("[Kanban] list \(node.id): items=\(data.items?.count ?? 0) virtualized=\(data.virtualized ?? true) axis=\(data.axis ?? 0) tpl=\(data.itemTemplate.type.map { "\($0)" } ?? "?")")
        let horizontal = (data.axis ?? props?.axis) == 1
        var cfg = UICollectionLayoutListConfiguration(appearance: .plain)
        cfg.backgroundColor = .clear
        cfg.showsSeparators = false
        let layout: UICollectionViewLayout
        if horizontal {
            let item = NSCollectionLayoutItem(layoutSize: .init(widthDimension: .estimated(data.estimatedItemExtent ?? 260),
                                                                heightDimension: .fractionalHeight(1)))
            let group = NSCollectionLayoutGroup.horizontal(layoutSize: .init(widthDimension: .estimated(data.estimatedItemExtent ?? 260),
                                                                             heightDimension: .fractionalHeight(1)), subitems: [item])
            let section = NSCollectionLayoutSection(group: group)
            section.interGroupSpacing = props?.spacing ?? 8
            layout = UICollectionViewCompositionalLayout(section: section)
        } else {
            layout = UICollectionViewCompositionalLayout.list(using: cfg)
        }

        let cv = UICollectionView(frame: .zero, collectionViewLayout: layout)
        cv.translatesAutoresizingMaskIntoConstraints = false
        cv.backgroundColor = .clear
        cv.alwaysBounceVertical = !horizontal
        cv.alwaysBounceHorizontal = horizontal

        // Cada célula constrói o ItemTemplate com o binding da linha.
        let template = data.itemTemplate
        // NB: captura FORTE de `self` de propósito — o builder é criado como local
        // no updateUIView e seria desalocado ao retornar; a cell registration é
        // LAZY (roda quando a célula é dequeued). Com [weak self] o builder morria
        // → células vazias. A collection view (via data source→registration) passa
        // a reter o builder pelo tempo de vida da lista. Sem ciclo: builder não
        // retém a cv.
        let registration = UICollectionView.CellRegistration<UICollectionViewCell, SduiListItem> { cell, _, item in
            NSLog("[Kanban] cell \(item.id) credor=\(item.data?["credor"] ?? "?")")
            cell.contentView.subviews.forEach { $0.removeFromSuperview() }
            self.bindingContext = item.data
            let itemNode = item.onTap != nil
                ? SduiNode(cloning: template, onTap: item.onTap)
                : template
            let v = self.build(node: itemNode)
            self.bindingContext = nil
            v.translatesAutoresizingMaskIntoConstraints = false
            cell.contentView.addSubview(v)
            NSLayoutConstraint.activate([
                v.topAnchor.constraint(equalTo: cell.contentView.topAnchor, constant: 3),
                v.leadingAnchor.constraint(equalTo: cell.contentView.leadingAnchor, constant: 6),
                v.trailingAnchor.constraint(equalTo: cell.contentView.trailingAnchor, constant: -6),
                v.bottomAnchor.constraint(equalTo: cell.contentView.bottomAnchor, constant: -3),
            ])
        }

        let ds = UICollectionViewDiffableDataSource<Int, String>(collectionView: cv) { cv, indexPath, id in
            let item = (data.items ?? []).first { $0.id == id } ?? SduiListItem(id: id, data: nil, onTap: nil)
            return cv.dequeueConfiguredReusableCell(using: registration, for: indexPath, item: item)
        }
        var snap = NSDiffableDataSourceSnapshot<Int, String>()
        snap.appendSections([0])
        snap.appendItems((data.items ?? []).map { $0.id })
        ds.apply(snap, animatingDifferences: false)
        NSLog("[Kanban] list \(node.id): snapshot applied \(snap.numberOfItems) items")

        // Retém o data source no próprio collection view.
        objc_setAssociatedObject(cv, &Self.dsKey, ds, .OBJC_ASSOCIATION_RETAIN)
        // A collection view não tem tamanho intrínseco: hugging baixo (preenche o
        // pai) + um piso pra nunca colapsar a 0 dentro de um stack.
        cv.setContentHuggingPriority(.defaultLow, for: horizontal ? .horizontal : .vertical)
        if horizontal {
            cv.heightAnchor.constraint(greaterThanOrEqualToConstant: 92).isActive = true
        } else {
            cv.heightAnchor.constraint(greaterThanOrEqualToConstant: 200).isActive = true
        }
        return cv
    }
    private static var dsKey: UInt8 = 0

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
        if let mn = props.minWidth { view.widthAnchor.constraint(greaterThanOrEqualToConstant: mn).isActive = true }
        if let mx = props.maxWidth { view.widthAnchor.constraint(lessThanOrEqualToConstant: mx).isActive = true }
        if let mn = props.minHeight { view.heightAnchor.constraint(greaterThanOrEqualToConstant: mn).isActive = true }
        if let mx = props.maxHeight { view.heightAnchor.constraint(lessThanOrEqualToConstant: mx).isActive = true }
        if let ar = props.aspectRatio, ar > 0 {
            view.widthAnchor.constraint(equalTo: view.heightAnchor, multiplier: ar).isActive = true
        }
    }

    private func applyPadding(_ edges: SduiEdges?, to stack: UIStackView) {
        guard let e = edges else { return }
        stack.isLayoutMarginsRelativeArrangement = true
        stack.layoutMargins = UIEdgeInsets(top: e.top, left: e.left, bottom: e.bottom, right: e.right)
    }

    private func alignment(_ align: UInt8?, axis: NSLayoutConstraint.Axis) -> UIStackView.Alignment {
        switch align ?? 3 {
        case 0: return axis == .horizontal ? .top : .leading
        case 1: return .center
        case 2: return axis == .horizontal ? .bottom : .trailing
        default: return .fill
        }
    }

    // MARK: - Acessibilidade

    private func applyA11y(_ a11y: SduiA11y?, to view: UIView) {
        guard let a = a11y else { return }
        if a.hidden == true { view.accessibilityElementsHidden = true; return }
        if let label = a.label { view.isAccessibilityElement = true; view.accessibilityLabel = label }
        if let hint = a.hint { view.accessibilityHint = hint }
        if let value = a.value { view.accessibilityValue = value }
        if let role = a.role { view.accessibilityTraits.formUnion(trait(forRole: role)) }
        if let t = a.traits { view.accessibilityTraits.formUnion(traits(fromFlags: t)) }
        if a.label != nil || a.role != nil { view.isAccessibilityElement = true }
    }

    private func trait(forRole role: UInt8) -> UIAccessibilityTraits {
        switch role {
        case 1: return .button
        case 2: return .header
        case 3: return .link
        case 4: return .image
        case 5: return .staticText
        case 6: return .adjustable
        case 7: return .searchField
        case 8: return .summaryElement
        case 10: return .updatesFrequently
        default: return .none
        }
    }

    private func traits(fromFlags f: UInt32) -> UIAccessibilityTraits {
        var t: UIAccessibilityTraits = .none
        if f & (1 << 0) != 0 { t.insert(.selected) }
        if f & (1 << 1) != 0 { t.insert(.notEnabled) }
        if f & (1 << 2) != 0 { t.insert(.updatesFrequently) }
        if f & (1 << 3) != 0 { t.insert(.playsSound) }
        if f & (1 << 4) != 0 { t.insert(.startsMediaSession) }
        if f & (1 << 5) != 0 { t.insert(.causesPageTurn) }
        return t
    }
}

// Clona um nó trocando só o onTap (usado por linhas de lista com tap próprio).
extension SduiNode {
    init(cloning n: SduiNode, onTap: SduiAction?) {
        self = SduiNode(id: n.id, typeRaw: n.typeRaw, props: n.props, children: n.children,
                        onTap: onTap, a11y: n.a11y, fallback: n.fallback,
                        minSchemaVersion: n.minSchemaVersion, responsive: n.responsive,
                        list: n.list, nav: n.nav, bind: n.bind)
    }
    // Init memberwise explícito (o custom init(from:) some com o sintetizado).
    init(id: String, typeRaw: UInt8, props: SduiProps?, children: [SduiNode]?,
         onTap: SduiAction?, a11y: SduiA11y?, fallback: UInt8?, minSchemaVersion: Int?,
         responsive: [SduiResponsiveOverride]?, list: SduiListData?, nav: SduiNav?, bind: [String: String]?) {
        self.id = id; self.typeRaw = typeRaw; self.props = props; self.children = children
        self.onTap = onTap; self.a11y = a11y; self.fallback = fallback
        self.minSchemaVersion = minSchemaVersion; self.responsive = responsive
        self.list = list; self.nav = nav; self.bind = bind
    }
}
