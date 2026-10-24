# -*- coding: utf-8 -*-
"""
诊断：罗盘页在 #scr 模式下光标为什么没被隐藏。

两路取证：
  A) 无界面 --dump-dom：直接读回页面里的 SCR 标志与各元素的 computed cursor
  B) 有窗口 --app 启动：读窗口标题（页面会把诊断串写进 title），
     验证 --app 启动时 URL 片段是否被保留（SCR 是否真的为真）
"""
import ctypes
import ctypes.wintypes as wt
import os
import re
import subprocess
import sys
import time
import shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP = os.path.join(os.environ.get('TEMP', '.'), 'tccur')
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
u32 = ctypes.windll.user32

DIAG_JS = """<script>
window.addEventListener('load',function(){setTimeout(function(){
  function cs(e){ return e? getComputedStyle(e).cursor : '(null)'; }
  function d(e){ return e? (e.tagName+(e.id?'#'+e.id:'')+(e.getAttribute&&e.getAttribute('class')?'.'+e.getAttribute('class'):'')) : '(null)'; }
  var e=document.elementFromPoint(innerWidth/2,innerHeight/2);
  var list=[];
  var chain=e; var n=0;
  while(chain && n<6){ list.push(d(chain)+'='+cs(chain)); chain=chain.parentElement; n++; }
  document.title='DIAG|SCR='+SCR+'|body='+cs(document.body)+'|docEl='+cs(document.documentElement)
    +'|hash='+location.hash+'|chain='+list.join(' < ');
},700);});
</script>
"""


def build_diag():
    os.makedirs(TMP, exist_ok=True)
    html = open(os.path.join(ROOT, '时间罗盘.html'), encoding='utf-8').read()
    assert '</body>' in html
    html = html.replace('</body>', DIAG_JS + '</body>', 1)
    dst = os.path.join(TMP, 'diag_scr.html')
    open(dst, 'w', encoding='utf-8').write(html)
    return dst


def dump_dom(dst):
    url = 'file:///' + dst.replace('\\', '/') + '#scr&q=eco'
    prof = os.path.join(TMP, 'prof_diag_dump')
    shutil.rmtree(prof, ignore_errors=True)
    r = subprocess.run([EDGE, '--headless=new', '--window-size=1200,800',
                        '--user-data-dir=' + prof, '--no-first-run',
                        '--virtual-time-budget=4000', '--dump-dom', url],
                       capture_output=True, timeout=180)
    dom = (r.stdout or b'').decode('utf-8', 'replace')
    m = re.search(r'<title>(.*?)</title>', dom, re.S)
    return m.group(1) if m else '(未取到 title)'


def window_title_with_app(dst):
    url = 'file:///' + dst.replace('\\', '/') + '#scr&q=eco'
    prof = os.path.join(TMP, 'prof_diag_app')
    shutil.rmtree(prof, ignore_errors=True)
    p = subprocess.Popen([EDGE, '--app=' + url, '--user-data-dir=' + prof,
                          '--no-first-run', '--no-default-browser-check',
                          '--disable-extensions', '--window-position=260,180',
                          '--window-size=1000,700'],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        time.sleep(9.0)
        hwnd = u32.GetForegroundWindow()
        buf = ctypes.create_unicode_buffer(1024)
        u32.GetWindowTextW(hwnd, buf, 1024)
        return buf.value
    finally:
        subprocess.run(['taskkill', '/T', '/F', '/PID', str(p.pid)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=20)
        time.sleep(1.0)


if __name__ == '__main__':
    dst = build_diag()
    print('DIAG 文件:', dst)
    print('\n[A] 无界面 dump-dom（#scr&q=eco）:')
    print('   ', dump_dom(dst))
    print('\n[B] 有窗口 --app 启动，读窗口标题:')
    print('   ', window_title_with_app(dst))
