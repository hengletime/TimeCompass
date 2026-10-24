# -*- coding: utf-8 -*-
"""流星渲染"像素级"验收：把流星强制固定在画面内，读取头部/中点/尾端的真实像素，
确认拖尾在黑底上确实可见（而不是只写了渐变代码却看不见）。

背景：0.9px 的斜线在抗锯齿下每像素只覆盖约 20%，中点像素 alpha 可能只有 20/255——
     肉眼看就是"没有拖尾"。这个脚本就是为了把这类"代码看着对、画面看不见"的问题量出来。

用法： python tools/probe_meteor.py
"""
import os, re, subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP = os.path.join(os.environ.get('TEMP', r'C:\Windows\Temp'), 'tcbench')
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
os.makedirs(TMP, exist_ok=True)

html = open(os.path.join(ROOT, '时间罗盘.html'), encoding='utf-8').read()

# 1) 流星必现，位置固定在画面内（正常逻辑是从屏幕外飞入，截图很难抓到）
OLD_PUSH = """    if(Math.random()<0.012&&meteors.length<3){
      const sp=4.2+Math.random()*3.4;
      meteors.push({x:W+20+Math.random()*160, y:Math.random()*H*.5,
        vx:-sp, vy:sp*(0.36+Math.random()*0.22),
        life:1, tail:14+Math.random()*8});}"""
NEW_PUSH = """    if(meteors.length<3){
      const k=meteors.length;
      meteors.push({x:W*(0.30+0.12*k), y:H*(0.08+0.12*k),
        vx:-6.5, vy:2.6, life:1, tail:18});}"""
assert OLD_PUSH in html, '未找到流星生成代码块'
d = html.replace(OLD_PUSH, NEW_PUSH)

# 2) 在绘制完成后，把第一条流星的头/中/尾三点像素写进 <title>，用 --dump-dom 回读
ANCHOR = "ctx.beginPath();ctx.arc(m.x,m.y,1.05,0,TAU);ctx.fill();"
i = d.rindex(ANCHOR)
DBG = """      ctx.globalAlpha=1;
      if(i===0){
        /* 取邻域最亮像素（人眼看到的"线的亮度"就是最亮处）。
           直接取单点会因 Math.round 的 0.5px 偏差落到 0.65px 宽的带外，读数偏低。 */
        var P=function(px,py){
          var d=ctx.getImageData(Math.round(px)-3,Math.round(py)-3,7,7).data,mx=0,r=0,g=0,b=0;
          for(var k=0;k<d.length;k+=4){ if(d[k+3]>mx){ mx=d[k+3]; r=d[k]; g=d[k+1]; b=d[k+2]; } }
          return r+','+g+','+b+',a'+mx;
        };
        document.title='流星像素 | 头 '+P(m.x,m.y)
          +' | 中 '+P((m.x+tx)/2,(m.y+ty)/2)
          +' | 尾 '+P(m.x*0.25+tx*0.75, m.y*0.25+ty*0.75)
          +' | 头端宽 '+(2*Math.hypot(ux,uy)).toFixed(2)+'px 拖尾长 '+Math.hypot(m.x-tx,m.y-ty).toFixed(0)+'px';
      }
    }"""
tail = d[i:].replace("""      ctx.globalAlpha=1;
    }""", DBG, 1)
assert tail != d[i:], '注入点不匹配'
d = d[:i] + tail

p = os.path.join(TMP, 'probe_meteor.html')
open(p, 'w', encoding='utf-8').write(d)

r = subprocess.run([EDGE, '--headless=new', '--enable-gpu', '--use-angle=d3d11',
                    '--window-size=1100,680', '--user-data-dir=' + os.path.join(TMP, 'prof_probe'),
                    '--no-first-run', '--disable-extensions', '--virtual-time-budget=1200',
                    '--dump-dom', 'file:///' + p.replace('\\', '/') + '#scr'],
                   capture_output=True, timeout=180)
dom = (r.stdout or b'').decode('utf-8', 'replace')
m = re.search(r'<title>(.*?)</title>', dom, re.S)
print(m.group(1) if m else '(未取到 title —— 检查脚本是否抛错)')
print('\n判读：中点 alpha 应 >= 100（旧版 0.9px stroke 实测只有 20，肉眼等于看不见）')
