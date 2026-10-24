# -*- coding: utf-8 -*-
"""生成"漂移帧率"消融变体：把每秒一步的漂移换成 N Hz 亚像素步进，
用于实测"平滑度 ↔ CPU 开销"的取舍曲线。"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP = os.path.join(os.environ.get('TEMP', r'C:\Windows\Temp'), 'tcbench')
SRC = os.path.join(ROOT, '时间罗盘.html')
os.makedirs(TMP, exist_ok=True)

html = open(SRC, encoding='utf-8').read()
OLD = "  if(SCR) driftStep();"
assert OLD in html, '未找到漂移调用行，请检查 HTML 结构'

TPL = ("  if(SCR&&!window.__dT){window.__dT=setInterval(function(){driftT++;var t=driftT/%d;"
       "var x=5*Math.sin(t*2*Math.PI/47),y=4*Math.sin(t*2*Math.PI/73+0.9);"
       "wrapEl.style.transform='translateX('+x.toFixed(3)+'vw)';"
       "dialEl.style.transform='translateY('+y.toFixed(3)+'vh)';},%d)}")

# 标签 → (每秒帧数, 帧间隔 ms)
VARIANTS = {'d10': 10, 'd20': 20, 'd30': 30, 'd60': 60}

print('源文件:', SRC)
for tag, hz in VARIANTS.items():
    ms = int(round(1000.0 / hz))
    # 关键：用 hz 作除数（driftT++ 的序号 ÷ hz = 秒），ms 才是定时器间隔
    new = TPL % (hz, ms)
    out = html.replace(OLD, new)
    assert out != html
    p = os.path.join(TMP, 'ab4_%s.html' % tag)
    open(p, 'w', encoding='utf-8').write(out)
    print('  生成 %-44s %d Hz (%d ms/帧)' % (p, hz, ms))

# 对照组：当前基线（1 Hz 步进）
import shutil
base = os.path.join(TMP, 'ab4_d1.html')
shutil.copyfile(SRC, base)
print('  生成 %-44s 1 Hz（现状）' % base)
print('\n接下来执行：')
print('  python tools/bench.py "漂移1Hz=%s" "漂移10Hz=%s" "漂移20Hz=%s" "漂移30Hz=%s" "漂移60Hz=%s"'
      % (base,
         os.path.join(TMP, 'ab4_d10.html'), os.path.join(TMP, 'ab4_d20.html'),
         os.path.join(TMP, 'ab4_d30.html'), os.path.join(TMP, 'ab4_d60.html')))
