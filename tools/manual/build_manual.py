# 遊び方（docs/manual/遊び方.md）から PDF を作る
#   11/26 たけらい：配る ZIP には PDF の遊び方を入れる（元の文章はこの Markdown 1つだけ）
#   使い方（リポジトリのいちばん上で）：
#     pip install markdown weasyprint
#     python tools/manual/build_manual.py
#   できあがり：TakeTaikoSimulator/遊び方.pdf（ビルドで exe の横にコピーされ、配る ZIP にも入る）
#   遊び方.md を直したら、これを動かして PDF もいっしょにコミットする
import datetime
import pathlib
import re

import markdown
from weasyprint import HTML

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs" / "manual" / "遊び方.md"
OUTPUT = ROOT / "TakeTaikoSimulator" / "遊び方.pdf"
REGULAR = ROOT / "TakeTaikoSimulator" / "Assets" / "Fonts" / "NotoSansJP-Regular.ttf"
BOLD = ROOT / "TakeTaikoSimulator" / "Skins" / "TaikoSim-Simple" / "Font" / "default.otf"   # Noto Sans CJK JP Bold
LOGO = ROOT / "TakeTaikoSimulator" / "Assets" / "Branding" / "Logo.png"

# ゲームの設定画面と同じ色（UI/Gui.cs）
ACCENT = "#e23a5e"      # 朱色
SAKURA = "#f9bcce"      # 桜色
INK = "#26171d"         # 濃い字

CSS = f"""
@font-face {{ font-family: "TTR"; src: url("{REGULAR.as_uri()}"); }}
@font-face {{ font-family: "TTB"; src: url("{BOLD.as_uri()}"); }}
@page {{
  size: A4; margin: 18mm 16mm 18mm 16mm;
  @bottom-center {{ content: counter(page) " / " counter(pages); font: 9pt "TTR"; color: #8a7a80; }}
}}
@page :first {{ @bottom-center {{ content: none; }} }}
html {{ font-family: "TTR"; font-size: 10.5pt; line-height: 1.7; color: {INK}; }}
.cover {{ page-break-after: always; text-align: center; padding-top: 55mm; }}
.cover img {{ width: 62mm; }}
.cover h1 {{ display: block; font-size: 30pt; margin: 10mm 0 2mm; border: none; color: {ACCENT}; }}
.cover p {{ color: #8a7a80; margin: 1mm 0; }}
h1 {{ display: none; }}
h2 {{ font-size: 17pt; color: #fff; background: {ACCENT}; padding: 2mm 4mm; border-radius: 2.5mm;
      margin: 0 0 4mm; page-break-before: always; }}
h3 {{ font-size: 13pt; color: {ACCENT}; border-bottom: 1.2pt solid {SAKURA}; padding-bottom: 1mm; margin: 6mm 0 3mm; }}
h4 {{ font-size: 11pt; margin: 4mm 0 2mm; }}
h3, h4 {{ page-break-after: avoid; }}
table {{ border-collapse: collapse; width: 100%; margin: 2mm 0 4mm; font-size: 9.5pt; page-break-inside: auto; }}
tr {{ page-break-inside: avoid; }}
th {{ background: #fbe3ea; color: {INK}; text-align: left; padding: 1.6mm 2.4mm; border-bottom: 1pt solid {ACCENT}; }}
td {{ padding: 1.6mm 2.4mm; border-bottom: 0.5pt solid #ecd9df; vertical-align: top; }}
td:first-child {{ overflow-wrap: anywhere; }}
th:first-child {{ width: 28%; }}
th:nth-child(3) {{ width: 24%; }}
td:last-child {{ overflow-wrap: anywhere; }}
code {{ font-family: "TTR"; background: #f6edf0; padding: 0.2mm 1.2mm; border-radius: 1mm; font-size: 9.5pt; }}
ul, ol {{ padding-left: 6mm; margin: 1mm 0 3mm; }}
li {{ margin: 0.6mm 0; }}
.admonition {{ border-left: 2.4mm solid {ACCENT}; background: #fdf2f5; padding: 2.4mm 4mm; border-radius: 1.6mm; margin: 3mm 0; page-break-inside: avoid; }}
.admonition.warning {{ border-color: #e8a317; background: #fff8e6; }}
.admonition.danger {{ border-color: #c0182f; background: #fdecee; }}
.admonition-title {{ margin: 0 0 1mm; }}
.toc {{ page-break-after: always; }}
.toc::before {{ content: "目次"; display: block; font-size: 17pt; color: {ACCENT}; margin-bottom: 4mm; }}
.toc ul {{ list-style: none; padding-left: 0; }}
.toc ul ul {{ padding-left: 6mm; font-size: 9.5pt; }}
.toc > ul > li > ul > li > ul {{ display: none; }}
.toc a {{ color: {INK}; text-decoration: none; }}
h2, h3, h4, th, td:first-child, strong, b, .admonition-title, .cover h1, .toc::before {{ font-family: "TTB"; font-weight: normal; }}
.toc a::after {{ content: leader('.') target-counter(attr(href), page); color: #8a7a80; }}
"""


def main():
    text = SOURCE.read_text(encoding="utf-8")
    body = markdown.markdown(
        text,
        extensions=["tables", "toc", "admonition", "md_in_html"],
        extension_configs={"toc": {"slugify": lambda value, sep: "s-" + str(abs(hash(value)) % 10**8)}},
    )
    today = datetime.date.today().strftime("%Y年%m月%d日")
    cover = f'<div class="cover"><img src="{LOGO.as_uri()}"><h1>TakeTaiko 遊び方</h1><p>操作・モード・設定ファイルの説明</p><p>{today} 版</p></div>'
    html = f'<!DOCTYPE html><html lang="ja"><head><meta charset="utf-8"><style>{CSS}</style></head><body>{cover}{body}</body></html>'
    HTML(string=html, base_url=str(ROOT)).write_pdf(OUTPUT)
    print(f"できました：{OUTPUT}（{OUTPUT.stat().st_size // 1024} KB）")


if __name__ == "__main__":
    main()
