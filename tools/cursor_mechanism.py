# -*- coding: utf-8 -*-
"""
指针隐藏机制——干净的两段式验证。

为什么必须"两段式"：Windows 的指针可见性是黏的，只有窗口在指针下发生变化时才会重新评估；
所以必须先制造一个"确定能把指针设回显示"的窗口（同类窗口 + 同线程 SetCursor(箭头)），
把它作为基线，再换成本方案的窗口去验证隐藏。否则测到的只是上一次的残留状态。

被验证的方案（将移植进 C# 屏保壳）：
  全屏 + 置顶 + 分层透明(alpha=1，不可见但可被指针命中) + 不激活的窗口；
  窗口类 hCursor=NULL，并在 WM_SETCURSOR 里 SetCursor(NULL) 且返回 1。
"""
import ctypes
import ctypes.wintypes as wt
import time

u32 = ctypes.windll.user32
k32 = ctypes.windll.kernel32

WS_POPUP, WS_VISIBLE = 0x80000000, 0x10000000
WS_EX_LAYERED, WS_EX_TOPMOST, WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE = 0x00080000, 0x8, 0x80, 0x08000000
LWA_ALPHA = 0x2
WM_SETCURSOR, WM_MOUSEACTIVATE = 0x20, 0x21
MA_NOACTIVATE = 3
VIRT_X, VIRT_Y, VIRT_W, VIRT_H = 76, 77, 78, 79
IDC_ARROW = 32512

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


u32.DefWindowProcW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
u32.DefWindowProcW.restype = ctypes.c_ssize_t
u32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEX)]
u32.CreateWindowExW.restype = wt.HWND
u32.LoadCursorW.restype = wt.HANDLE
u32.SendInput.argtypes = [wt.UINT, ctypes.c_void_p, ctypes.c_int]
u32.SendInput.restype = wt.UINT

ST = {'hide': False, 'setcursor': 0, 'hit': 0}


def wndproc(h, m, w, l):
    if m == WM_SETCURSOR:
        ST['setcursor'] += 1
        if ST['hide']:
            u32.SetCursor(None)
            return 1
    if m == WM_MOUSEACTIVATE:
        return MA_NOACTIVATE
    return u32.DefWindowProcW(h, m, w, l)


PROC = WNDPROC(wndproc)


def state():
    ci = CURSORINFO()
    ci.cbSize = ctypes.sizeof(CURSORINFO)
    u32.GetCursorInfo(ctypes.byref(ci))
    return '显示中=%s hCursor=%s' % (bool(ci.flags & 1), ci.hCursor or 0)


def pump(sec):
    msg = MSG()
    end = time.time() + sec
    while time.time() < end:
        while u32.PeekMessageW(ctypes.byref(msg), None, 0, 0, 1):
            u32.TranslateMessage(ctypes.byref(msg))
            u32.DispatchMessageW(ctypes.byref(msg))
        time.sleep(0.01)


def nudge(dx, dy):
    p = wt.POINT()
    u32.GetCursorPos(ctypes.byref(p))
    vx, vy = u32.GetSystemMetrics(VIRT_X), u32.GetSystemMetrics(VIRT_Y)
    vw, vh = u32.GetSystemMetrics(VIRT_W), u32.GetSystemMetrics(VIRT_H)
    tx = min(max(p.x + dx, vx), vx + vw - 1)
    ty = min(max(p.y + dy, vy), vy + vh - 1)
    inp = INPUT()
    inp.type = 0
    inp.mi.dx = int(round((tx - vx) * 65535.0 / max(vw - 1, 1)))
    inp.mi.dy = int(round((ty - vy) * 65535.0 / max(vh - 1, 1)))
    inp.mi.dwFlags = 0x1 | 0x8000 | 0x4000
    u32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def make(cls, hide):
    ST['hide'] = hide
    wc = WNDCLASSEX()
    wc.cbSize = ctypes.sizeof(WNDCLASSEX)
    wc.lpfnWndProc = PROC
    wc.hInstance = k32.GetModuleHandleW(None)
    wc.lpszClassName = cls
    wc.hCursor = None if hide else u32.LoadCursorW(None, IDC_ARROW)
    assert u32.RegisterClassExW(ctypes.byref(wc)), 'RegisterClassEx 失败'
    w, h = u32.GetSystemMetrics(VIRT_W), u32.GetSystemMetrics(VIRT_H)
    hwnd = u32.CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                               cls, '', WS_POPUP | WS_VISIBLE, 0, 0, w, h,
                               None, None, wc.hInstance, None)
    assert hwnd, 'CreateWindowEx 失败 err=%d' % k32.GetLastError()
    u32.SetLayeredWindowAttributes(hwnd, 0, 1, LWA_ALPHA)     # alpha=1 → 不可见但可命中
    return hwnd, wc.hInstance


def main():
    w, h = u32.GetSystemMetrics(VIRT_W), u32.GetSystemMetrics(VIRT_H)
    cx, cy = w // 2, h // 2
    print('虚拟屏 %dx%d  初始:%s' % (w, h, state()))

    # ---- 段 1：建立"显示中"基线 ----
    a, ia = make('TCProbeShow', False)
    u32.SetCursorPos(cx, cy)
    pump(0.3)
    u32.SetCursor(a, None)          # 该窗口在指针下 → 本线程 SetCursor 生效
    pump(0.3)
    u32.SetCursor(u32.LoadCursorW(None, IDC_ARROW))
    pump(0.3)
    s1 = state()
    print('① 基线（同类窗口带箭头光标）: %s' % s1)
    u32.DestroyWindow(a)
    u32.UnregisterClassW('TCProbeShow', ia)
    pump(0.2)

    # ---- 段 2：换成"不设光标"的窗口 ----
    b, ib = make('TCProbeHide', True)
    u32.SetCursorPos(cx, cy)
    u32.SetCursor(None)             # 建窗后立刻隐藏（不等鼠标移动）
    pump(0.4)
    s2 = state()
    print('② 隐藏窗口出现后立刻（未动鼠标）: %s' % s2)
    for i in range(5):
        nudge(3 if i % 2 == 0 else -3, 0)
        pump(0.12)
    s3 = state()
    print('③ 注入真实鼠标移动后            : %s' % s3)
    pump(1.5)
    s4 = state()
    print('④ 静置 1.5s 后                 : %s' % s4)
    print('   WM_SETCURSOR 收到 %d 次' % ST['setcursor'])

    u32.DestroyWindow(b)
    u32.UnregisterClassW('TCProbeHide', ib)
    pump(0.3)
    nudge(6, 6)
    pump(0.3)
    print('⑤ 销毁窗口并移动鼠标后          : %s' % state())

    ok = ('显示中=True' in s1) and ('显示中=False' in s2) and ('显示中=False' in s3) \
         and ('显示中=False' in s4)
    print('\n结论：%s' % ('✔ 方案成立（基线可显示 → 隐藏窗口下始终隐藏）' if ok
                        else '✗ 未达预期'))
    return 0 if ok else 2


if __name__ == '__main__':
    raise SystemExit(main())
