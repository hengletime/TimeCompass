# -*- coding: utf-8 -*-
"""指针幕布独立自检（v3.2）
目的：站在"旁观者"角度验证幕布是否真的把指针藏住，并量出它的 CPU 成本。

为什么不能只看壳自己的日志：壳说"已隐藏"是它自己报的。
外部观察者用 GetCursorInfo 采样，配合"不注入任何鼠标事件"的静置环境，
才能证明"指针从头到尾就没露出来过"。

用法：python tools/curtain_selfcheck.py [秒数]
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

u32 = ctypes.windll.user32
k32 = ctypes.windll.kernel32
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCR = os.path.join(ROOT, '屏保', 'TimeCompass.scr')
DIAG = os.path.join(os.environ.get('TEMP', '.'), 'tc_diag.log')


class CURSORINFO(ctypes.Structure):
    _fields_ = [('cbSize', wt.DWORD), ('flags', wt.DWORD),
                ('hCursor', ctypes.c_void_p), ('pt', wt.POINT)]


class FILETIME(ctypes.Structure):
    _fields_ = [('lo', wt.DWORD), ('hi', wt.DWORD)]


u32.GetCursorInfo.argtypes = [ctypes.POINTER(CURSORINFO)]
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
PROCESS_QUERY_INFORMATION = 0x0400


def showing():
    ci = CURSORINFO()
    ci.cbSize = ctypes.sizeof(CURSORINFO)
    if not u32.GetCursorInfo(ctypes.byref(ci)):
        return None
    return bool(ci.flags & 1)


def to100ns(ft):
    return (ft.hi << 32) | ft.lo


def cpu_seconds(pid):
    h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return None
    try:
        cr, ex, ker, usr = FILETIME(), FILETIME(), FILETIME(), FILETIME()
        if not k32.GetProcessTimes(h, ctypes.byref(cr), ctypes.byref(ex),
                                   ctypes.byref(ker), ctypes.byref(usr)):
            return None
        return (to100ns(ker) + to100ns(usr)) / 1e7
    finally:
        k32.CloseHandle(h)


def kill_all():
    for nm in ('TimeCompass.scr', '时间罗盘.scr'):
        subprocess.run(['taskkill', '/F', '/IM', nm],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    secs = int(sys.argv[1]) if len(sys.argv) > 1 else 15
    if not os.path.exists(SCR):
        print('找不到产物:', SCR)
        return 1
    kill_all()
    time.sleep(0.4)
    try:
        os.remove(DIAG)
    except OSError:
        pass

    base = showing()
    print('① 静置基线：指针显示中 = %s   （应为 True，否则说明系统本来就是隐藏态，测不出东西）' % base)

    p = subprocess.Popen([SCR, '/curtain', str(secs), '/diag'], cwd=os.path.dirname(SCR))
    timeline, cpu0, t0 = [], None, time.time()
    try:
        while True:
            time.sleep(0.1)                 # 100ms 粒度：抓得住 <100ms 的瞬时闪现
            t = time.time() - t0
            if p.poll() is not None:
                break
            if cpu0 is None:
                cpu0 = cpu_seconds(p.pid)
            timeline.append((round(t, 2), showing()))
            if t > secs + 2:
                break
        cpu1 = cpu_seconds(p.pid) if p.pid else None
        wall = time.time() - t0
    finally:
        try:
            p.wait(timeout=6)
        except Exception:
            kill_all()
        time.sleep(0.5)

    # 最长连续"可见"时长：判断是采样噪声还是真漏点
    longest = cur = 0.0
    start = None
    for t, s in timeline:
        if s:
            if start is None:
                start = t
            cur = t - start
            longest = max(longest, cur)
        else:
            start = None
    n_vis = sum(1 for _, s in timeline if s)

    print('\n② 幕布期间外部旁观采样（100ms 粒度，全程未注入任何鼠标事件）：')
    print('   采样 %d 次，其中"可见" %d 次（占 %.1f%%），最长连续可见 ≈ %.2f 秒'
          % (len(timeline), n_vis, 100.0 * n_vis / max(1, len(timeline)), longest))

    after = showing()
    print('\n③ 幕布撤掉后：指针显示中 = %s   （应为 True）' % after)

    if cpu0 is not None and cpu1 is not None and wall > 0:
        cpus = (cpu1 - cpu0) / wall
        print('\n④ 幕布自身开销：CPU 时间 %.3fs / %.1fs 墙钟 = %.2f%% of one core'
              % (cpu1 - cpu0, wall, cpus / 4.0 * 100))

    if os.path.exists(DIAG):
        print('\n--- 壳的诊断日志 ---')
        print(open(DIAG, encoding='utf-8', errors='replace').read().strip())

    ok = (base is True) and n_vis == 0 and after is True and len(timeline) >= 5
    print('\n结论：%s' % ('OK 幕布期间指针全程隐藏（0 次可见），撤销后正常恢复'
                        if ok else 'FAIL（可见 %d 次/最长 %.2fs，恢复=%s，采样=%d）'
                                  % (n_vis, longest, after, len(timeline))))
    return 0 if ok else 2


if __name__ == '__main__':
    sys.exit(main())
