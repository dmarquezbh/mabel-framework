#!/usr/bin/env python3
"""Gera um descritor SDUI v2 de teste que exercita a Onda 2:
NavStack + push/pop, List virtualizada (30 itens, binding), a11y (header),
fallback (tipo desconhecido 200 → placeholder), responsivo (fontSize por size-class)."""
import json

# type codes
SCREEN, VSTACK, HSTACK, SCROLL, LIST, CARD, TEXT, BUTTON, IMAGE, BADGE, PROG, DIV, SPACER, NAV = \
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14

ACCENT = 0x0074FFFF
PANEL = 0xFFFFFFFF
TEXTC = 0x1A1A2EFF
TEXT3 = 0x6C757DFF
SOFT = 0xE6F0FFFF

def node(id, type, **kw):
    n = {"id": id, "type": type}
    n.update({k: v for k, v in kw.items() if v is not None})
    return n

# --- itemTemplate do List: Card(HStack[Text bind credor (flex), Badge bind valor]) ---
item_template = node("tpl:card", CARD,
    props={"cornerRadius": 8, "background": PANEL, "borderColor": SOFT, "borderWidth": 1,
           "padding": {"top": 10, "right": 12, "bottom": 10, "left": 12}, "spacing": 4},
    a11y={"role": 1, "hint": "Abre o detalhe da operação"},   # button
    onTap={"name": "open-card", "navigate": {"kind": 0, "route": "detail"}},
    children=[
        node("tpl:row", HSTACK, props={"spacing": 8, "align": 1}, children=[
            node("tpl:credor", TEXT, props={"fontSize": 15, "color": TEXTC, "weight": 2, "flexGrow": 1},
                 bind={"text": "credor"}),
            node("tpl:valor", BADGE, props={"fontSize": 12, "color": ACCENT, "background": SOFT, "cornerRadius": 4},
                 bind={"text": "valor"}),
        ]),
        node("tpl:sub", TEXT, props={"fontSize": 12, "color": TEXT3}, bind={"text": "etapa"}),
    ])

CREDORES = ["Construtora ÁlAn S.", "Marina Q. de O.", "Transportes VLR", "José R. Prado",
            "Ana P. Nogueira", "Metalúrgica GTR", "Cláudia M. Reis", "Paulo Andrade",
            "Agropecuária SND", "Renata F. Lima", "Incorporadora PLT", "Sérgio Toledo"]
ETAPAS = ["Cadastro", "Triagem", "Diligência", "Precificação", "Contrato", "Pagamento"]
items = []
for i in range(30):
    items.append({
        "id": f"card:{50000 + i}",
        "data": {"credor": CREDORES[i % len(CREDORES)], "etapa": ETAPAS[i % len(ETAPAS)],
                 "valor": f"R$ {((i * 137) % 900) + 100}k"},
    })

board_list = node("list:ops", LIST,
    list={"itemTemplate": item_template, "items": items, "virtualized": True,
          "axis": 0, "estimatedItemExtent": 74, "count": 30})

# Header (a11y header role) + título responsivo (fontSize por size-class)
header = node("hdr", TEXT,
    props={"text": "Ledgerções", "fontSize": 22, "weight": 3, "color": TEXTC,
           "padding": {"top": 8, "right": 16, "bottom": 8, "left": 16}},
    a11y={"role": 2, "label": "Lista de operações do kanban"},   # header
    responsive=[
        {"widthClass": 2, "props": {"fontSize": 34}},   # regular (iPad/landscape) → maior
        {"widthClass": 1, "props": {"fontSize": 22}},   # compact → padrão
    ])

# Nó de tipo DESCONHECIDO (200) com fallback placeholder → prova degradação graciosa
future = node("future:widget", 200, fallback=1,
              children=[node("future:child", TEXT, props={"text": "(oculto — filho do nó futuro)"})])

home = node("screen:home", SCREEN,
    props={"safeArea": 1, "background": 0xF8F9FAFF},
    nav={"route": "home", "title": "Org — Onda 2"},
    children=[node("home:v", VSTACK, props={"spacing": 8}, children=[header, future, board_list])])

detail = node("screen:detail", SCREEN,
    props={"safeArea": 1, "background": PANEL},
    nav={"route": "detail", "title": "Detalhe"},
    children=[node("det:v", VSTACK, props={"spacing": 16, "padding": {"top": 24, "right": 16, "bottom": 16, "left": 16}}, children=[
        node("det:title", TEXT, props={"text": "Detalhe da operação", "fontSize": 20, "weight": 2, "color": TEXTC}),
        node("det:body", TEXT, props={"text": "Tela empilhada via NavStack (push). Toque em Voltar.", "fontSize": 15, "color": TEXT3}),
        node("det:back", BUTTON, props={"text": "← Voltar", "color": ACCENT},
             onTap={"name": "back", "navigate": {"kind": 1}}),   # pop
    ])])

root = node("nav:root", NAV, children=[home, detail])
doc = {"schemaVersion": 2, "root": root}

out = "/home/user/apps/another-project/ios_app/Sources/ios_app/Resources/kanban-sdui.json"
open(out, "w", encoding="utf-8").write(json.dumps(doc, ensure_ascii=False))
print("wrote", out, "items:", len(items))
