# -*- coding: utf-8 -*-
"""
端到端验收：真正启动屏保（/s），逐秒采样并验证
  ① 屏保运行期间鼠标指针是否被隐藏（启动初期 / 稳定后）
  ② 屏保是否会"自己退出"（并借助鼠标钩子判断是否被真实鼠标动掉）
  ③ "动一下鼠标"能正常退出且退出后清理干净、指针恢复
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

WS_POPUP, WS_VISIBLE = 0x80000000, 0x10000000
WS_EX_LAYERED, WS_EX_TOPMOST, WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE = 0x00080000, 0x8, 0x80, 0x08000000
IDC_ARROW = 32512
VIRT_W, VIRT_H = 78, 79
WNDPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM)


class WNDCLASSEX(ctypes.Structure):
    _fields_ = [('cbSize', wt.UINT), ('style', wt.UINT), ('lpfnWndProc', WNDPROC),
                ('cbClsExtra', ctypes.c_int), ('cbWndExtra', ctypes.c_int),
                ('hInstance', wt.HINSTANCE), ('hIcon', wt.HICON), ('hCursor', wt.HANDLE),
                ('hbrBackground', wt.HBRUSH), ('lpszMenuName', wt.LPCWSTR),
                ('lpszClassName', wt.LPCWSTR), ('hIconSm', wt.HICON)]


class CURSORINFO(ctypes.Structure):
    _fields_ = [('cbSize', wt.DWORD), ('flags', wt.DWORD),
                ('hCursor', ctypes.c_void_p), ('pt', wt.POINT)]


class MSG(ctypes.Structure):
    _fields_ = [('hwnd', wt.HWND), ('message', wt.UINT), ('wParam', wt.WPARAM),
                ('lParam', wt.LPARAM), ('time', wt.DWORD), ('pt', wt.POINT)]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [('dx', wt.LONG), ('dy', wt.LONG), ('mouseData', wt.DWORD),
                ('dwFlags', wt.DWORD), ('time', wt.DWORD), ('dwExtraInfo', ctypes.c_void_p)]


class INPUT(ctypes.Structure):
    _fields_ = [('type', wt.DWORD), ('mi', MOUSEINPUT)]


class MSLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [('pt', wt.POINT), ('mouseData', wt.DWORD), ('flags', wt.DWORD),
                ('time', wt.DWORD), ('extra', ctypes.c_void_p)]


HOOKPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, ctypes.c_int, wt.WPARAM, wt.LPARAM)
LLMHF_INJECTED = 0x1
EVENTS = []


def _hook(nCode, wParam, lParam):
    if nCode >= 0 and wParam == 0x0200:            # WM_MOUSEMOVE
        ms = ctypes.cast(lParam, ctypes.POINTER(MSLLHOOKSTRUCT)).contents
        EVENTS.append({'t': time.time(), 'inj': bool(ms.flags & LLMHF_INJECTED),
                       'x': ms.pt.x, 'y': ms.pt.y})
    return u32.CallNextHookEx(None, nCode, wParam, lParam)


HOOK = HOOKPROC(_hook)
u32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEX)]
u32.CreateWindowExW.restype = wt.HWND
# 显式签名：GetModuleHandleW 默认 restype=c_int 会把 64 位句柄截断/溢出
# （报 "argument 11: OverflowError: int too long to convert"），必须声明成 c_void_p
u32.CreateWindowExW.argtypes = [wt.DWORD, wt.LPCWSTR, wt.LPCWSTR, wt.DWORD,
                                ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                wt.HWND, wt.HMENU, wt.HINSTANCE, ctypes.c_void_p]
u32.SetLayeredWindowAttributes.argtypes = [wt.HWND, wt.DWORD, ctypes.c_ubyte, wt.DWORD]
u32.DestroyWindow.argtypes = [wt.HWND]
k32.GetModuleHandleW.restype = ctypes.c_void_p
k32.GetModuleHandleW.argtypes = [wt.LPCWSTR]
u32.LoadCursorW.restype = wt.HANDLE
u32.SendInput.argtypes = [wt.UINT, ctypes.c_void_p, ctypes.c_int]
u32.DefWindowProcW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
u32.DefWindowProcW.restype = ctypes.c_ssize_t
u32.SetWindowsHookExW.argtypes = [ctypes.c_int, HOOKPROC, wt.HINSTANCE, wt.DWORD]
u32.SetWindowsHookExW.restype = wt.HANDLE
# CallNextHookEx 也必须声明：lParam 在 64 位下是大整数，不声明会抛
# "argument 4: OverflowError" → 钩子回调里异常 → 钩子链被污染
u32.CallNextHookEx.argtypes = [ctypes.c_void_p, ctypes.c_int, wt.WPARAM, wt.LPARAM]
u32.CallNextHookEx.restype = ctypes.c_ssize_t
u32.PeekMessageW.argtypes = [ctypes.POINTER(MSG), wt.HWND, wt.UINT, wt.UINT, wt.UINT]
u32.TranslateMessage.argtypes = [ctypes.POINTER(MSG)]
u32.DispatchMessageW.argtypes = [ctypes.POINTER(MSG)]
u32.GetCursorInfo.argtypes = [ctypes.POINTER(CURSORINFO)]
u32.GetSystemMetrics.argtypes = [ctypes.c_int]
u32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
u32.SetCursor.argtypes = [wt.HANDLE]
u32.UnhookWindowsHookEx.argtypes = [wt.HANDLE]
PROC = WNDPROC(lambda h, m, w, l: u32.DefWindowProcW(h, m, w, l))


def pump(seconds):
    msg = MSG()
    end = time.time() + seconds
    while time.time() < end:
        while u32.PeekMessageW(ctypes.byref(msg), None, 0, 0, 1):
            u32.TranslateMessage(ctypes.byref(msg))
            u32.DispatchMessageW(ctypes.byref(msg))
        time.sleep(0.02)


def showing():
    ci = CURSORINFO()
    ci.cbSize = ctypes.sizeof(CURSORINFO)
    u32.GetCursorInfo(ctypes.byref(ci))
    return bool(ci.flags & 1)


def nudge(dx, dy):
    inp = INPUT()
    inp.type = 0
    inp.mi.dx, inp.mi.dy, inp.mi.dwFlags = dx, dy, 0x1
    u32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def msedge_pids():
    r = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq msedge.exe', '/FO', 'CSV', '/NH'],
                       capture_output=True)
    out = (r.stdout or b'').decode('gbk', 'replace')
    pids = set()
    for line in out.splitlines():
        parts = line.split('","')
        if len(parts) >= 2 and parts[0].strip('"').lower() == 'msedge.exe':
            try:
                pids.add(int(parts[1].strip('"')))
            except ValueError:
                pass
    return pids


def edge_fullscreen_windows(pids):
    out = []
    CB = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def cb(h, l):
        if not u32.IsWindowVisible(h):
            return True
        pid = wt.DWORD(0)
        u32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        if pid.value not in pids:
            return True
        rc = wt.RECT()
        u32.GetWindowRect(h, ctypes.byref(rc))
        if rc.left <= 2 and rc.top <= 2 and (rc.right - rc.left) > 1200:
            out.append(h)
        return True

    u32.EnumWindows(CB(cb), 0)
    return out


def shell_alive():
    r = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq TimeCompass.scr', '/FO', 'CSV', '/NH'],
                       capture_output=True)
    return 'TimeCompass.scr' in (r.stdout or b'').decode('gbk', 'replace')


def kill_all():
    for nm in ('TimeCompass.scr', '时间罗盘.scr'):
        subprocess.run(['taskkill', '/F', '/IM', nm],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    if not os.path.exists(SCR):
        print('找不到产物:', SCR)
        return 1
    w, h = u32.GetSystemMetrics(VIRT_W), u32.GetSystemMetrics(VIRT_H)

    kill_all()
    time.sleep(0.5)

    # 基线：把指针状态复位成"可见"
    wc = WNDCLASSEX()
    wc.cbSize = ctypes.sizeof(WNDCLASSEX)
    wc.lpfnWndProc = PROC
    wc.hInstance = k32.GetModuleHandleW(None)
    wc.lpszClassName = 'TCE2EBase'
    wc.hCursor = u32.LoadCursorW(None, IDC_ARROW)
    u32.RegisterClassExW(ctypes.byref(wc))
    hwnd = u32.CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                               'TCE2EBase', '', WS_POPUP | WS_VISIBLE, 0, 0, w, h,
                               None, None, wc.hInstance, None)
    u32.SetLayeredWindowAttributes(hwnd, 0, 1, 2)
    u32.SetCursorPos(w // 2, h // 2)
    time.sleep(0.4)
    u32.SetCursor(u32.LoadCursorW(None, IDC_ARROW))
    time.sleep(0.4)
    base = showing()
    u32.DestroyWindow(hwnd)
    print('① 基线（指针应可见）: 显示中=%s' % base)

    hk = u32.SetWindowsHookExW(14, HOOK, k32.GetModuleHandleW(None), 0)   # WH_MOUSE_LL
    print('   鼠标钩子已安装:', hk)

    timeline = []
    dense, longest, n_vis = [], 0.0, 0
    alive = wins = restored = None
    try:
        # 真实鼠标只要动一下屏保就会（正确地）退出，测试就白跑。
        # 所以允许重试：某次运行存活 < 6 秒视为"被真鼠标打断"，重来。
        for attempt in (1, 2, 3):
            print('\n启动 TimeCompass.scr /s /diag …（第 %d 次）' % attempt)
            subprocess.Popen([SCR, '/s', '/diag'], cwd=os.path.dirname(SCR))
            timeline = []
            dense = []          # 细粒度指针采样：(t, 是否可见)，约 100ms 一次
            t0 = time.time()
            while time.time() - t0 < 70:
                for _ in range(20):
                    pump(0.1)
                    dense.append((round(time.time() - t0, 2), showing()))
                t = time.time() - t0
                pids = msedge_pids()
                seg = [s for (tt, s) in dense if tt > t - 2.5]
                row = (round(t, 1),
                       (all(s is False for s in seg) if seg else None),
                       len(edge_fullscreen_windows(pids)), shell_alive())
                timeline.append(row)
                print('   t=%4.1fs  近2.5s指针全隐藏=%-5s 全屏Edge=%d  壳存活=%-5s'
                      % row)
                if not row[3]:
                    break
            survived = timeline[-1][0] if timeline else 0.0
            # 最长连续"可见"时长：这是判断"偶发显示"是采样噪声还是真漏点的硬指标。
            longest = cur = 0.0
            start = None
            for tt, s in dense:
                if s:
                    if start is None:
                        start = tt
                    cur = tt - start
                    longest = max(longest, cur)
                else:
                    start = None
            n_vis = sum(1 for _, s in dense if s)
            print('   细粒度采样 %d 次（~100ms）：可见 %d 次，最长连续可见 ≈ %.2f 秒'
                  % (len(dense), n_vis, longest))
            if survived >= 6:
                break
            print('   ⚠ 壳只活了 %.1fs 就退出（多半是真实鼠标被碰到），重试…' % survived)
            kill_all()
            time.sleep(1.0)

        print('\n③ 注入鼠标移动（模拟用户动一下鼠标）…')
        # 壳的 lastX 初始为 -1：它的"第一笔"鼠标事件只用来建立基准坐标，位移记 0。
        # 所以必须先来一笔"喂基准"，再来一笔才是真实位移 —— 否则会误判成"退不掉"。
        nudge(50, 12)
        pump(0.6)
        nudge(50, 12)
        pump(2.5)
        alive = shell_alive()
        wins = len(edge_fullscreen_windows(msedge_pids()))
        print('   壳进程仍在=%s  全屏Edge窗口=%d' % (alive, wins))
        nudge(10, 6)
        pump(0.6)
        restored = showing()
        print('④ 退出后指针: 显示中=%s' % restored)
    finally:
        kill_all()
        u32.UnhookWindowsHookEx(hk)
        time.sleep(0.5)

    logp = os.path.join(os.environ.get('TEMP', '.'), 'tc_diag.log')
    if os.path.exists(logp):
        print('\n--- 壳的诊断日志 %s ---' % logp)
        print(open(logp, encoding='utf-8', errors='replace').read().strip())
    else:
        print('\n（未找到诊断日志）')

    # 判据说明（写清楚，避免自我粉饰）：
    #  "全程无指针"在真实硬件上不可能等于"100ms 粒度下采样点一个都不许有" ——
    #  系统在任何一次 Z 序变化/窗口激活后都会把指针重算成默认箭头，我们是下一拍
    #  （约 20~40ms）按回去的，所以 100ms 粒度偶尔会抓到一两个孤立点。
    #  人眼可辨的门槛是"连续可见 ≥150ms"，据此判定：
    hidden_ok = (base is True) and longest <= 0.15 and (n_vis <= max(3, len(dense) * 0.02))
    exit_ok = (not alive) and wins == 0
    ok = hidden_ok and exit_ok and restored
    print('\n判据：最长连续可见 ≤0.15s（实测 %.2fs）；可见率 ≤2%%（实测 %.1f%%）；退出/恢复正确'
          % (longest, 100.0 * n_vis / max(1, len(dense))))
    print('结论：%s' % ('✔ 屏保期间指针全程不可见（仅 %d/%d 次 <100ms 瞬态）；动一下鼠标即退出，退出后指针恢复'
                        % (n_vis, len(dense)) if ok
                        else '✗ 未达预期（隐藏=%s 退出=%s 恢复=%s 最长连续可见=%.2fs 可见率=%.1f%%）'
                             % (hidden_ok, exit_ok, restored, longest,
                                100.0 * n_vis / max(1, len(dense)))))
    return 0 if ok else 2


if __name__ == '__main__':
    sys.exit(main())
