
import os, re, glob, sys

ROOT = "src/CrashScope.Desktop"
xamls = sorted(glob.glob(ROOT + "/**/*.xaml", recursive=True))
cses  = sorted(glob.glob(ROOT + "/**/*.cs", recursive=True))

def rd(p):
    return open(p, encoding="utf-8-sig", errors="replace").read()

# ---------- defined keys ----------
KEY = re.compile(r'x:Key="([^"]+)"')
defined = {}          # key -> set(files)
for f in xamls:
    for k in KEY.findall(rd(f)):
        if k.startswith("{"):
            continue
        defined.setdefault(k, set()).add(os.path.basename(f))

DARK  = "DarkTheme.xaml"
LIGHT = "LightTheme.xaml"
dark_keys  = {k for k,v in defined.items() if DARK  in v}
light_keys = {k for k,v in defined.items() if LIGHT in v}
all_defined = set(defined)

# ---------- referenced keys ----------
REF = re.compile(r'\{(?:Dynamic|Static)Resource\s+([^}\s]+)\s*\}')
refs = {}
for f in xamls:
    for k in REF.findall(rd(f)):
        if k.startswith("{") or ":" in k:
            continue
        refs.setdefault(k, set()).add(os.path.basename(f))

CSREF = re.compile(r'(?:Try)?FindResource\(\s*"([^"]+)"|Resources\[\s*"([^"]+)"\s*\]|\bResolve\(\s*"([^"]+)"')
for f in cses:
    for m in CSREF.finditer(rd(f)):
        k = m.group(1) or m.group(2) or m.group(3)
        refs.setdefault(k, set()).add(os.path.basename(f))

out = []
def p(s=""): out.append(s)

p("=" * 62)
p("A. 资源键引用闭环审计")
p("=" * 62)
p("xaml files: %d   cs files: %d" % (len(xamls), len(cses)))
p("defined keys: %d   referenced keys: %d" % (len(all_defined), len(refs)))
p("")

unresolved = {k: v for k, v in refs.items() if k not in all_defined}
p("--- A1. 引用了但任何地方都没定义（运行时静默失败）---")
if not unresolved:
    p("  NONE")
else:
    for k in sorted(unresolved):
        p("  !! %-28s <- %s" % (k, ", ".join(sorted(unresolved[k]))))
p("")

theme_refs = {k: v for k, v in refs.items() if k in dark_keys or k in light_keys}
onesided = {k: v for k, v in theme_refs.items()
            if not (k in dark_keys and k in light_keys)}
p("--- A2. 被引用的主题键只在单边主题定义（切主题即坏）---")
if not onesided:
    p("  NONE")
else:
    for k in sorted(onesided):
        side = "Dark only" if k in dark_keys else "Light only"
        p("  !! %-28s [%s] <- %s" % (k, side, ", ".join(sorted(onesided[k]))))
p("")

p("--- A3. 主题键 parity（全量，不限被引用的）---")
p("  Dark=%d  Light=%d" % (len(dark_keys), len(light_keys)))
p("  Dark-only : %s" % (sorted(dark_keys - light_keys) or "[]"))
p("  Light-only: %s" % (sorted(light_keys - dark_keys) or "[]"))
p("")

dead = sorted((dark_keys | light_keys) - set(refs))
p("--- A4. 主题里定义了但无人引用（死键，非阻塞）---")
p("  %d: %s" % (len(dead), dead if dead else "[]"))
p("")

# ---------- colors / contrast ----------
COLOR_TAG   = re.compile(r'<Color\s+x:Key="([^"]+)"\s*>\s*(#[0-9A-Fa-f]{6,8})\s*</Color>')
BRUSH_TAG   = re.compile(r'<SolidColorBrush\s+x:Key="([^"]+)"[^>]*?Color="(#[0-9A-Fa-f]{6,8})"')
BRUSH_TAG2  = re.compile(r'<SolidColorBrush\s+x:Key="([^"]+)"\s*>\s*(#[0-9A-Fa-f]{6,8})\s*</SolidColorBrush>')

def colors_of(path):
    t = rd(path); d = {}
    for rx in (COLOR_TAG, BRUSH_TAG, BRUSH_TAG2):
        for k, v in rx.findall(t):
            d[k] = v
    return d

def parse(h):
    h = h.lstrip("#")
    if len(h) == 8:
        return int(h[0:2],16)/255.0, (int(h[2:4],16), int(h[4:6],16), int(h[6:8],16))
    return 1.0, (int(h[0:2],16), int(h[2:4],16), int(h[4:6],16))

def lin(c):
    c = c/255.0
    return c/12.92 if c <= 0.04045 else ((c+0.055)/1.055) ** 2.4

def lum(rgb):
    r,g,b = [lin(x) for x in rgb]
    return 0.2126*r + 0.7152*g + 0.0722*b

def over(fg_hex, bg_rgb):
    a, rgb = parse(fg_hex)
    return tuple(a*rgb[i] + (1-a)*bg_rgb[i] for i in range(3))

def ratio(fg_hex, bg_hex):
    _, bg = parse(bg_hex)
    f = over(fg_hex, bg)
    l1, l2 = lum(f), lum(bg)
    if l1 < l2: l1, l2 = l2, l1
    return (l1+0.05)/(l2+0.05)

p("=" * 62)
p("B. 对比度审计（WCAG，自动，无需肉眼）")
p("=" * 62)
SURFACES = ["ContentBg","ContentBgAlt","CardBg","CardBgAlt"]
for theme_file in (DARK, LIGHT):
    path = os.path.join(ROOT, "Themes", theme_file)
    if not os.path.exists(path):
        p("  (missing %s)" % theme_file); continue
    c = colors_of(path)
    p("")
    p("### %s  (解析到 %d 个颜色)" % (theme_file, len(c)))
    texts = [k for k in c if re.search(r'Text|Foreground', k)]
    surfs = [s for s in SURFACES if s in c]
    if texts and surfs:
        p("  %-16s %s" % ("text \\ surface", "  ".join("%-12s" % s for s in surfs)))
        for t in sorted(texts):
            cells = []
            for s in surfs:
                r = ratio(c[t], c[s])
                flag = "OK " if r >= 4.5 else ("aa-" if r >= 3.0 else "!! ")
                cells.append("%-12s" % ("%s%.2f" % (flag, r)))
            p("  %-16s %s" % (t, "  ".join(cells)))
    # accent text on saturated fills
    p("  -- OnAccentText 落在饱和填充之上 --")
    if "OnAccentText" in c:
        for fill in ["AccentColor","SuccessColor","WarningColor","DangerColor",
                     "SeverityCritical","SeverityHigh","SeverityMedium","SeverityLow"]:
            if fill in c:
                r = ratio(c["OnAccentText"], c[fill])
                flag = "OK " if r >= 4.5 else ("aa-" if r >= 3.0 else "!! ")
                p("     %s %-18s %.2f" % (flag, fill, r))
    else:
        p("     (OnAccentText not parsed)")

p("")
p("图例: OK>=4.5 正文合格 | aa- 3.0..4.5 仅大字号合格 | !! <3.0 不合格")
# ---------- E. code-resolved brushes must be refreshed on theme change ----------
p("")
p("=" * 62)
p("E. 代码解析的画刷 / 图表是否接了 ThemeChanged")
p("=" * 62)
p("SeverityPalette.Resolve 与 ChartPalette.Resolve 在调用时就把画刷定死了，")
p("页面不订阅 ThemeChanged 就会在切主题后保留旧配色（视觉孤岛）。")
p("")
# Only item-model brushes matter: a brush stored on a list/chart view-model object
# survives until the list is rebuilt. A brush assigned straight to a control's
# Foreground is refreshed on the next update (and status toasts self-dismiss),
# so those are not stale-colour risks.
ITEM_BRUSH = re.compile(r'public\s+(?:System\.Windows\.Media\.)?Brush\s+\w+\s*\{\s*get;\s*set;')
HAS_PLOT = re.compile(r'\bPlotModel\b')
stale = []
for f in cses:
    if "/Pages/" not in f.replace("\\", "/"):
        continue
    t = rd(f)
    holds_item_brush = ITEM_BRUSH.search(t) and re.search(
        r'\b(?:SeverityPalette|ChartPalette)\.Resolve\b', t)
    if (holds_item_brush or HAS_PLOT.search(t)) and "ThemeChanged" not in t:
        stale.append(os.path.basename(f))
if stale:
    for s in sorted(stale):
        p("  !! %s 用了主题画刷但未订阅 ThemeChanged" % s)
else:
    p("  NONE")

p("")
p("--- 订阅/退订对称性（事件泄漏）---")
leaks = []
for f in cses:
    t = rd(f)
    a, r_ = t.count("ThemeChanged +="), t.count("ThemeChanged -=")
    if a and r_ < a:
        leaks.append("%s (+=%d -=%d)" % (os.path.basename(f), a, r_))
if leaks:
    for l in leaks:
        p("  !! " + l)
else:
    p("  NONE")

sys.stdout.reconfigure(encoding="utf-8")
print("\n".join(out))

# exit code so this can gate CI: only real defects fail.
# L.* keys are injected at runtime by LocalizationService and are expected to be absent from XAML.
hard = [k for k in unresolved if not k.startswith("L.")]
fails = []
if hard:
    fails.append("unresolved non-localization resource keys: %s" % sorted(hard))
if onesided:
    fails.append("theme keys defined on only one side: %s" % sorted(onesided))
if dark_keys ^ light_keys:
    fails.append("theme parity broken: %s" % sorted(dark_keys ^ light_keys))
if stale:
    fails.append("pages using themed brushes without a ThemeChanged subscription: %s" % sorted(stale))
if leaks:
    fails.append("ThemeChanged subscribed more often than unsubscribed: %s" % leaks)
if fails:
    print("\nFAIL:")
    for f in fails:
        print("  - " + f)
    sys.exit(1)
print("\nPASS: no unresolved non-localization keys, theme parity intact.")
sys.exit(0)

