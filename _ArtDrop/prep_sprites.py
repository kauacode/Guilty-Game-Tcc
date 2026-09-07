"""
Prepara as texturas do menu de interrogatorio para uso como Sprite na Unity.

Para cada imagem solta na pasta de entrada:
  1. detecta o fundo por flood fill a partir das BORDAS, nao por "toda cor
     clara" — o papel amassado e quase branco, e um corte por limiar global
     abriria buracos no meio dele;
  2. transforma esse fundo em alpha, com a borda suavizada;
  3. recorta na bounding box do conteudo, para o RectTransform da UI
     coincidir com o desenho;
  4. reduz a resolucao ao necessario para UI;
  5. decide qual textura e qual COMPARANDO as imagens entre si e salva com o
     nome que o InterrogationMenuBuilder espera.

Uso: python prep_sprites.py <pasta_entrada> <pasta_saida> [tolerancia]
"""
import os, sys
from collections import deque
from PIL import Image, ImageFilter

SRC = sys.argv[1]
DST = sys.argv[2]
TOL = int(sys.argv[3]) if len(sys.argv) > 3 else 32

# Lado maior maximo do sprite final. Acima disso e memoria de textura jogada
# fora: o maior elemento da prancheta ocupa ~700px na resolucao de referencia.
MAX_SIDE = 1280

NAMES = {
    "clipboard": "UI_Clipboard.png",
    "aged":      "UI_PaperAged.png",
    "crumpled":  "UI_PaperCrumpled.png",
    "button":    "UI_ButtonRed.png",
}


def background_to_alpha(im, tol=TOL):
    """Flood fill a partir das bordas. Devolve RGBA ja recortada."""
    im = im.convert("RGB")
    w, h = im.size
    px = im.load()

    corners = [px[0, 0], px[w - 1, 0], px[0, h - 1], px[w - 1, h - 1]]
    bg = tuple(sorted(c[i] for c in corners)[1] for i in range(3))

    def is_bg(c):
        return (abs(c[0] - bg[0]) <= tol and
                abs(c[1] - bg[1]) <= tol and
                abs(c[2] - bg[2]) <= tol)

    mask = bytearray(w * h)
    q = deque()

    for x in range(w):
        for y in (0, h - 1):
            if not mask[y * w + x] and is_bg(px[x, y]):
                mask[y * w + x] = 1
                q.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if not mask[y * w + x] and is_bg(px[x, y]):
                mask[y * w + x] = 1
                q.append((x, y))

    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h:
                i = ny * w + nx
                if not mask[i] and is_bg(px[nx, ny]):
                    mask[i] = 1
                    q.append((nx, ny))

    alpha = Image.frombytes("L", (w, h), bytes(255 if not m else 0 for m in mask))
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.8))

    out = im.convert("RGBA")
    out.putalpha(alpha)

    bbox = alpha.point(lambda v: 255 if v > 8 else 0).getbbox()
    if bbox:
        out = out.crop(bbox)
    return out, bg


def downscale(im, max_side=MAX_SIDE):
    w, h = im.size
    if max(w, h) <= max_side:
        return im
    k = max_side / max(w, h)
    return im.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)


def features(im):
    """Cor media dos pixels OPACOS — o fundo transparente nao entra na conta."""
    small = im.resize((64, 64), Image.LANCZOS).convert("RGBA")
    r = g = b = n = 0
    for pr, pg, pb, pa in list(small.getdata()):
        if pa < 128:
            continue
        r += pr; g += pg; b += pb; n += 1
    if n == 0:
        return 0.0, 0.0, 0.0
    return r / n, g / n, b / n


os.makedirs(DST, exist_ok=True)

files = [f for f in sorted(os.listdir(SRC))
         if f.lower().endswith((".png", ".jpg", ".jpeg", ".webp"))]
if not files:
    print(f"NENHUMA imagem encontrada em {SRC}")
    sys.exit(1)

# ── etapa 1: recorta todas e mede ───────────────────────────────────────────
items = []
for f in files:
    im = Image.open(os.path.join(SRC, f))
    w0, h0 = im.size
    cut, bg = background_to_alpha(im)
    cut = downscale(cut)
    r, g, b = features(cut)
    items.append({
        "file": f, "img": cut, "bg": bg,
        "lum":  (r + g + b) / 3,
        "red":  r - (g + b) / 2,     # quanto o vermelho domina
        "warm": r - b,               # quanto puxa para creme/sepia
    })
    print(f"  lido {f}: {w0}x{h0} -> {cut.size[0]}x{cut.size[1]}  "
          f"| fundo RGB{bg} | lum {(r+g+b)/3:.0f} red {r-(g+b)/2:+.0f} warm {r-b:+.0f}")

print()

# ── etapa 2: atribuicao RELATIVA ────────────────────────────────────────────
# Comparar as imagens entre si em vez de contra limiares fixos. Os dois papeis
# sao ambos claros e ambos meio quentes: um limiar fixo de "quao bege e"
# colocou os dois do mesmo lado e descartou um como duplicata. A ordem importa
# — tira primeiro os dois casos inconfundiveis (vermelho e escuro), e so entao
# separa os dois papeis, que ai sao os unicos restantes.
pool = list(items)
assigned = {}

if pool:
    pick = max(pool, key=lambda d: d["red"])       # o mais vermelho = botao
    assigned["button"] = pick; pool.remove(pick)
if pool:
    pick = min(pool, key=lambda d: d["lum"])       # o mais escuro = prancheta
    assigned["clipboard"] = pick; pool.remove(pick)
if len(pool) >= 2:
    pool.sort(key=lambda d: d["warm"], reverse=True)
    assigned["aged"]     = pool[0]                 # o mais creme
    assigned["crumpled"] = pool[1]                 # o mais cinza
    pool = pool[2:]
elif len(pool) == 1:
    assigned["aged"] = pool[0]; pool = []

for kind, item in assigned.items():
    path = os.path.join(DST, NAMES[kind])
    item["img"].save(path)
    w, h = item["img"].size
    print(f"  {item['file']}")
    print(f"      -> {NAMES[kind]}   {w}x{h}  (aspect {w/h:.3f})")

for extra in pool:
    print(f"  ! {extra['file']}: sobrou, nenhuma categoria livre — ignorado.")

faltando = [NAMES[k] for k in NAMES if k not in assigned]
print()
if faltando:
    print("FALTANDO: " + ", ".join(faltando))
else:
    print("As 4 texturas foram geradas.")
