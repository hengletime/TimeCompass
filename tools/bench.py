"""时间罗盘 · 屏保性能基准（前后对比，可复跑）

思路：用本机真实 Edge 以无界面方式加载 HTML 的 #scr 屏保模式，
      运行固定时长，统计 msedge 进程树的 CPU 秒数 → 换算成"占几个核"。
      全程无窗口弹出，不干扰使用。

用法：
  python tools/bench.py 基线            # 当前 时间罗盘.html
  python tools/bench.py 优化后
  python tools/bench.py A B --secs 30   # 同一轮里连测两份（同机同条件，最可比）

产物：tools/bench_result.json
"""
import json, os, shutil, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP = os.path.join(os.environ.get('TEMP', r'C:\Windows\Temp'), 'tcbench')
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
SRC = os.path.join(ROOT, '时间罗盘.html')
PS = os.path.join(TMP, 'cpu.ps1')

WARMUP = 18          # 预热秒数（让 Edge 完成首帧、JIT、缓存）
SAMPLE = 30          # 采样窗口秒数

# ---------------------------------------------------------------- PowerShell
PS_CODE = r'''
param([string]$Profile, [switch]$Kill)
$procs = Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
         Where-Object { $_.CommandLine -like "*$Profile*" }
if ($Kill) {
  $n = 0
  foreach ($p in $procs) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; $n++ } catch {} }
  Write-Output "killed=$n"
  exit
}
$sum = 0.0; $cnt = 0
foreach ($p in $procs) {
  try { $sum += (Get-Process -Id $p.ProcessId -ErrorAction Stop).CPU; $cnt++ } catch {}
}
Write-Output ("{0}|{1}" -f $cnt, $sum.ToString([Globalization.CultureInfo]::InvariantCulture))
'''


def cpu(profile, kill=False):
    r = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                        '-File', PS, profile] + (['-Kill'] if kill else []),
                       capture_output=True, timeout=90)
    out = (r.stdout or b'').decode('utf-8', 'replace').strip()
    if kill:
        return out
    n, s = out.split('|')
    return int(n), float(s)


def stage(tag, src=None):
    """把待测 HTML 复制到纯 ASCII 临时路径（规避命令行中文编码问题）"""
    os.makedirs(TMP, exist_ok=True)
    dst = os.path.join(TMP, 'src_%s.html' % tag)
    shutil.copyfile(src or SRC, dst)
    prof = os.path.join(TMP, 'prof_%s' % tag)
    shutil.rmtree(prof, ignore_errors=True)
    return dst, prof


def run(tag, src=None):
    dst, prof = stage(tag, src)
    url = 'file:///' + dst.replace('\\', '/') + '#scr'
    args = [EDGE,
            '--headless=new', '--enable-gpu', '--use-angle=d3d11',
            '--window-size=1920,1080', '--force-device-scale-factor=1',
            '--user-data-dir=' + prof,
            '--no-first-run', '--no-default-browser-check', '--disable-extensions',
            '--disable-component-extensions-with-background-pages',
            '--autoplay-policy=no-user-gesture-required', '--mute-audio',
            '--disable-features=TranslateUI', url]
    print('  [%s] 启动 Edge 无界面渲染 …' % tag)
    subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(WARMUP)
    n0, c0 = cpu(prof)
    t0 = time.time()
    time.sleep(SAMPLE)
    n1, c1 = cpu(prof)
    wall = time.time() - t0
    print(cpu(prof, kill=True))
    time.sleep(1.0)
    shutil.rmtree(prof, ignore_errors=True)
    cores = (c1 - c0) / wall
    res = {'tag': tag, 'procs': n1, 'cpuSeconds': round(c1 - c0, 2),
           'wall': round(wall, 1), 'cores': round(cores, 3),
           'pctOfOneCpu': round(cores * 100, 1)}
    print('  [%s] 进程数 %d | CPU %.2fs / %.1fs → 相当于 %.2f 个核（单核的 %.0f%%）'
          % (tag, n1, c1 - c0, wall, cores, cores * 100))
    return res


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('-')]
    if not args:
        args = ['基线']
    if not os.path.exists(PS):
        os.makedirs(TMP, exist_ok=True)
        with open(PS, 'w', encoding='ascii') as f:
            f.write(PS_CODE)
    print('被测文件:', SRC)
    out = []
    for a in args:
        # 支持 "标签=HTML路径" 形式做消融对比
        tag, src = a.split('=', 1) if '=' in a else (a, None)
        out.append(run(tag, src))
    p = os.path.join(ROOT, 'tools', 'bench_result.json')
    json.dump(out, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    print('\n结果已写入', p)
    for r in out:
        print('  %-10s %.2f 核  (%.0f%% of 1 core)' % (r['tag'], r['cores'], r['pctOfOneCpu']))


if __name__ == '__main__':
    main()
