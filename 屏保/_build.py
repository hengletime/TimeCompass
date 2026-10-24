"""屏保构建 + 验证（不依赖 shell 工具，规避 PATH 故障）
用法： python 屏保/_build.py
"""
import os, re, shutil, subprocess, sys

DIR = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(DIR, '构建屏保.bat')
AUTO = os.path.join(DIR, 'build_auto.bat')
SCR = os.path.join(DIR, 'TimeCompass.scr')
CN = os.path.join(DIR, '时间罗盘.scr')
HTML = os.path.join(os.path.dirname(DIR), '时间罗盘.html')


def run_build():
    """复制构建脚本并去掉 pause（避免等待按键），然后执行"""
    # v4.0 (D-18)：bat 用通配符 "..\\*.html" 取父目录的 html —— 出现第二个 .html
    # 会静默嵌错文件，构建前先做唯一性预检
    import glob
    htmls=glob.glob(os.path.join(os.path.dirname(DIR),'*.html'))
    if len(htmls)!=1 or os.path.abspath(htmls[0])!=os.path.abspath(HTML):
        print('!! 父目录 .html 不唯一或不含目标文件，bat 通配符会嵌错：',htmls)
        sys.exit(1)
    with open(SRC, 'r', encoding='utf-8') as f:
        lines = [ln for ln in f if ln.strip().lower() != 'pause']
    # v4.0 (D-19)：build_auto.bat 以 ascii 重写，非 ASCII 行会被静默吞掉 —— 预检拦下
    bad=[ln.rstrip() for ln in lines if any(ord(c)>127 for c in ln)]
    if bad:
        print('!! 构建脚本含非 ASCII 行（ascii 重写会静默吞掉）：',bad[:3])
        sys.exit(1)
    with open(AUTO, 'w', encoding='ascii', errors='ignore') as f:
        f.writelines(lines)
    r = subprocess.run(['cmd', '/c', 'build_auto.bat'], cwd=DIR,
                       capture_output=True, stdin=subprocess.DEVNULL)
    out = (r.stdout or b'').decode('gbk', errors='replace')
    errs = [l for l in out.splitlines() if 'error' in l.lower()]
    print('编译错误:', errs if errs else 'NONE')
    # v4.0 (D-17)：csc 的编译消息是本地化的（中文系统输出"错误 CSxxxx"），
    # 只靠 stdout 含 "error" 判错会漏检 → 必须以退出码为准
    if errs or r.returncode != 0:
        print('!! 构建失败 returncode=%d' % r.returncode)
        print(out[-800:])
        sys.exit(1)


def verify():
    """校验产物：PE 头 + 内嵌 HTML 关键片段"""
    # v4.0 (D-17)：陈旧产物断言 —— .scr 的 mtime 必须新于 Scr.cs 与 HTML，
    # 否则下面所有 marker 校验都在给旧产物"盖章放行"（compile 失败漏检的后果）
    scr_mt = os.path.getmtime(SCR)
    for src in (os.path.join(DIR,'Scr.cs'), HTML):
        if os.path.getmtime(src) > scr_mt:
            print('!! 产物比源码旧：%s 比 TimeCompass.scr 新 —— .scr 是陈旧产物' % os.path.basename(src))
            sys.exit(1)
    data = open(SCR, 'rb').read()
    html = open(HTML, 'r', encoding='utf-8').read()
    print('产物大小:', len(data), '| MZ 头:', data[:2] == b'MZ')
    # 资源表校验（权威）
    try:
        import dnfile
        pe = dnfile.dnPE(SCR)
        print('内嵌资源表:', [r.Name.value for r in pe.net.mdtables.ManifestResource.rows])
        methods = [m.Name.value for m in pe.net.mdtables.MethodDef.rows]
        print('v2.0 屏保壳完整 (MonitorLoop):', 'MonitorLoop' in methods)
    except ImportError:
        print('（未安装 dnfile，跳过资源表校验）')
    # 关键片段：确认内嵌的是最新 HTML，而不是旧版
    for marker in ['respawn', 'CORNERS', '260px', 'try{', 'tickOn=false']:
        print('  含 %-14s: %s' % (marker, marker.encode('utf-8') in data))
    stale = '0.5+0.5*Math.sin(t*st.s+st.p)'
    print('  旧固定星代码已清除:', stale.encode('utf-8') not in data)
    # v3.x 性能/观感标记：漂移改为"按时间求值 + 亚像素步进"，流星改为"渐细填充彗尾"
    for marker in ['driftApply', 'startDrift', 'driftMs', 'Q_PRESET', 'starGen',
                   'steps(32)', 'createLinearGradient']:
        print('  含 %-14s: %s' % (marker, marker.encode('utf-8') in data))
    for marker in [b'requestAnimationFrame(tick)', b'requestAnimationFrame(loop)',
                   b'@keyframes scrDX', b'@keyframes scrDY', b'if(SCR) driftStep()']:
        print('  旧高耗代码已清除 %-32s: %s' % (marker.decode(), marker not in data))
    # v3.1/v3.2 屏保壳标记：指针幕布 + 确定性隐藏 + 热键候选 + 诊断日志
    # 方法名/字段名在 #Strings 堆里是 UTF-8，而"字符串字面量"在 #US 堆里是 UTF-16，
    # 所以两类标记要用不同编码去找（这点踩过坑：字面量按 UTF-8 找会误报 False）。
    for marker in ['RegisterEmergencyHotkey', 'DestroyCursorCurtain', 'CreateCursorCurtain',
                   'CurtainWndProc', 'SPI_SETCURSORS',
                   'HideCursorTick', 'CursorShowing', 'GetCursorInfo', 'ValidateRect',
                   'CURSOR_SHOWING', 'setCursorMsgHits', 'selfHealCount', 'HIDE_TICK_MS']:
        print('  壳含(UTF-8)  %-26s: %s' % (marker, marker.encode('utf-8') in data))
    for literal in ['TimeCompassCursorCurtain', 'tc_diag.log']:
        print('  壳含(UTF-16) %-26s: %s' % (literal, literal.encode('utf-16-le') in data))
    for lit in ['时间罗盘动态屏保 v4.0', '指针自愈', '依次尝试的目录与失败原因',
                'tc_probe_', '启动策略 ', '可写目录判定：']:
        print('  壳含版本号/中文串 %-14s: %s' % (lit[:14], lit.encode('utf-16-le') in data))
    # v3.2 源码级硬校验：幕布只能建在 MonitorLoop（唯一持续泵消息的线程）里，
    # Main 主流程不得再裸调 CreateCursorCurtain —— 这正是本次"指针不隐藏"的根因①。
    # 注意 /curtain 自检分支本身就要用幕布，所以只检查"排除自检分支"的前后两段。
    src = open(os.path.join(DIR, 'Scr.cs'), encoding='utf-8').read()
    loop_body = src.split('static void MonitorLoop()')[1].split('/* ================= 入口')[0]
    main_body = src.split('static void Main(string[] args)')[1]
    main_pre = main_body.split('if(a.StartsWith("/curtain")')[0]
    main_tail = main_body.split('/* v3.2：先起监控线程')[1]
    checks = [
        ('根因① CreateCursorCurtain 建在 MonitorLoop 内', 'CreateCursorCurtain();' in loop_body),
        ('根因① MonitorLoop 末尾销毁幕布', 'DestroyCursorCurtain();' in loop_body),
        ('根因① 主流程已无裸调 CreateCursorCurtain',
         'CreateCursorCurtain();' not in main_pre and 'CreateCursorCurtain();' not in main_tail),
        ('根因① 主流程已不再调用 DestroyCursorCurtain',
         'DestroyCursorCurtain();' not in main_pre and 'DestroyCursorCurtain();' not in main_tail),
        ('根因③ 隐藏循环在监控线程每拍调用', 'HideCursorTick();' in loop_body),
        ('根因③ 自愈闭环使用 GetCursorInfo', 'CursorShowing()' in loop_body),
        # v3.3：按需抬窗（主人实测"启动时指针闪 4 次才隐藏"）
        ('v3.3 有 Z 序侦察 SomethingAbove', 'SomethingAbove(' in loop_body),
        ('v3.3 有 FindEdgeWindow', 'FindEdgeWindow()' in loop_body),
        ('v3.3 无条件周期抬窗已移除', 'pinning' not in loop_body),
        ('v3.3 抬窗后 15ms 补刀隐藏', 'Thread.Sleep(15);' in loop_body),
        # v3.9：4 策略启动链 + cmd 委托写探针 + 不误杀用户窗口
        ('v3.9 4 条策略名齐全', '④最小参数+系统默认配置' in src),
        ('v3.9 看门狗走策略状态机', 'NextEdgeStrategy();' in loop_body),
        ('v3.9 有 cmd 委托写探针', 'TC_WRITE_OK' in src and 'RunCmd(' in src),
        ('v3.9 探测结果只探一次', 'wdirProbed' in src),
        ('v3.9 退出不误杀用户窗口', 'preEdge.Contains(w)' in src),
        ('v3.9 启动前给已有 Edge 窗口拍快照', 'SnapEdgeWindows();' in main_body),
        # v4.0 全面审查修复（D-xx 对应审查报告）
        ('v4.0 D-02 成功判定=快照外新窗口', 'EdgeWindowAppeared()' in loop_body
            and 'EdgeKioskWindowExists' not in src
            and src.count('!preEdge.Contains(h)') >= 2
            and 'IsFullScreenish(h) || !preEdge.Contains(h)' not in src),
        ('v4.0 D-01 策略超时已加长', 'strategyMs={30000,20000,40000,25000}' in src),
        ('v4.0 D-03 诊断盘后置初始化', 'InitDiagDisk()' in src),
        ('v4.0 D-03 幕布先行于探测', main_tail.index('mt.Start();') < main_tail.index('InitDiagDisk();')),
        ('v4.0 D-04 钩子诊断走内存队列', 'HookDiag(' in src and 'FlushHookDiag();' in loop_body),
        ('v4.0 D-05 私有 profile 主线程预建', 'PreCreateProfiles();' in main_body),
        ('v4.0 D-06 策略切换冷却', 'resumeAt' in src and 'pendingIdx' in src),
        ('v4.0 D-08 回收交接窗口', 'CloseForeignEdgeWindows();' in src),
        ('v4.0 D-09 HTTP 循环读头', 'IndexOf("\\r\\n\\r\\n")' in src),
        ('v4.0 D-10 死代码已删除', 'static bool EdgeWindowExists' not in src),
        ('v4.0 D-11 稳态零枚举', 'lastEdgeSeen<30000' in src),
        ('v4.0 D-13 minimal 补 autoplay', src.count('autoplay-policy=no-user-gesture-required') >= 2),
    ]
    bad = [n for n, ok in checks if not ok]
    for n, ok in checks:
        print('  %s %s' % ('OK ' if ok else 'BAD', n))
    if bad:
        print('!! 源码硬校验未通过：%s' % bad)
        sys.exit(1)
    ok_order = main_tail.index('mt.Start();') < main_tail.index('LaunchEdge(url);')
    print('  %s 主线程先起监控线程（挂幕布）再启动 Edge' % ('OK ' if ok_order else 'BAD'))
    if not ok_order:
        sys.exit(1)
    # 内嵌 HTML 与磁盘 HTML 的一致性抽查
    probes = ['function startStars', 'function respawn', 'cornerIdx=(cornerIdx+1)',
              'function driftApply', 'const Q_PRESET', 'createLinearGradient']
    for p in probes:
        if p.encode('utf-8') in data and p in html:
            print('  与磁盘 HTML 一致:', p)
        else:
            print('  ⚠ 不一致:', p)
            sys.exit(1)


def sync_copies():
    try:
        shutil.copyfile(SCR, CN)
        print('中文名副本已同步')
    except PermissionError:
        print('⚠ 中文名副本被占用（正在预览屏保），退出预览后重跑本脚本即可')
    finally:
        if os.path.exists(AUTO):
            os.remove(AUTO)
            print('临时构建脚本已清理')


if __name__ == '__main__':
    run_build()
    verify()
    sync_copies()
