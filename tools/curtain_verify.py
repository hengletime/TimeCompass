# -*- coding: utf-8 -*-
"""
验证已构建的屏保产物里的「指针幕布」：
  ① 先建立"指针可见"的基线（同类窗口 + 同线程 SetCursor(箭头)）
  ② 启动 TimeCompass.scr /curtain N（只挂幕布，不启动屏保）
  ③ 检查指针是否被隐藏（并在幕布存在期间保持隐藏）
  ④ 结束后检查指针是否自动恢复
并顺带量一下幕布本身的系统开销（GetSystemTimes 前后差值）。
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

u32 = ctypes.windll.user32
k32 = ctypes.windll.kernel32
SCR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   '屏保', 'TimeCompass.scr')

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


class FILETIME(ctypes.Structure):
    _fields_ = [('lo', wt.DWORD), ('hi', wt.DWORD)]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [('dx', wt.LONG), ('dy', wt.LONG), ('mouseData', wt.DWORD),
                ('dwFlags', wt.DWORD), ('time', wt.DWORD), ('dwExtraInfo', ctypes.c_void_p)]


class INPUT(ctypes.Structure):
    _fields_ = [('type', wt.DWORD), ('mi', MOUSEINPUT)]


u32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEX)]
u32.CreateWindowExW.restype = wt.HWND
u32.DefWindowProcW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
u32.DefWindowProcW.restype = ctypes.c_ssize_t
u32.LoadCursorW.restype = wt.HANDLE
u32.SendInput.argtypes = [wt.UINT, ctypes.c_void_p, ctypes.c_int]

PROC = WNDPROC(lambda h, m, w, l: u32.DefWindowProcW(h, m, w, l))


def cursor_showing():
    ci = CURSORINFO()
    ci.cbSize = ctypes.sizeof(CURSORINFO)
    u32.GetCursorInfo(ctypes.byref(ci))
    return bool(ci.flags & 1)


def ft2f(f):
    return (f.hi << 32) | f.lo


def cpu_snapshot():
    i, k, u = FILETIME(), FILETIME(), FILETIME()
    k32.GetSystemTimes(ctypes.byref(i), ctypes.byref(k), ctypes.byref(u))
    return ft2f(i), ft2f(k) + ft2f(u)


def cpu_busy(seconds):
    """返回该时段内"非空闲"CPU 占比（所有进程合计）"""
    i0, b0 = cpu_snapshot()
    time.sleep(seconds)
    i1, b1 = cpu_snapshot()
    di, db = i1 - i0, b1 - b0
    tot = di + db
    return 0.0 if tot <= 0 else db / float(tot)


def make_arrow_window(w, h):
    """建一个"带箭头光标"的同类窗口，用于把指针状态复位成可见"""
    wc = WNDCLASSEX()
    wc.cbSize = ctypes.sizeof(WNDCLASSEX)
    wc.lpfnWndProc = PROC
    wc.hInstance = k32.GetModuleHandleW(None)
    wc.lpszClassName = 'TCCurtainVerify'
    wc.hCursor = u32.LoadCursorW(None, IDC_ARROW)
    u32.RegisterClassExW(ctypes.byref(wc))
    hwnd = u32.CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                               'TCCurtainVerify', '', WS_POPUP | WS_VISIBLE, 0, 0, w, h,
                               None, None, wc.hInstance, None)
    u32.SetLayeredWindowAttributes(hwnd, 0, 1, 2)
    return hwnd


def nudge(dx, dy):
    p = wt.POINT()
    u32.GetCursorPos(ctypes.byref(p))
    inp = INPUT()
    inp.type = 0
    inp.mi.dx = min(max(p.x + dx, 0), 65535)
    inp.mi.dy = min(max(p.y + dy, 0), 65535)
    inp.mi.dwFlags = 0x1
    u32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def main():
    if not os.path.exists(SCR):
        print('找不到产物:', SCR)
        return 1
    w, h = u32.GetSystemMetrics(VIRT_W), u32.GetSystemMetrics(VIRT_H)
    print('被测产物:', SCR, '| 屏幕 %dx%d' % (w, h))

    print('\n[开销基线] 空转 4 秒…')
    idle = cpu_busy(4)
    print('   系统 CPU 占用 = %.1f%%' % (idle * 100))

    # ① 复位成"可见"
    hwnd = make_arrow_window(w, h)
    u32.SetCursorPos(w // 2, h // 2)
    time.sleep(0.4)
    u32.SetCursor(u32.LoadCursorW(None, IDC_ARROW))
    time.sleep(0.4)
    base = cursor_showing()
    print('\n① 基线（指针应为可见）: 显示中=%s' % base)
    u32.DestroyWindow(hwnd)
    time.sleep(0.4)

    # ② 启动幕布
    p = subprocess.Popen([SCR, '/curtain', '12'], cwd=os.path.dirname(SCR))
    time.sleep(2.5)
    s1 = cursor_showing()
    print('② 幕布启动 2.5 秒后     : 显示中=%s' % s1)
    busy = cpu_busy(4)
    print('   幕布存在期间系统 CPU 占用 = %.1f%%（对比空转 %.1f%%）' % (busy * 100, idle * 100))
    nudge(9, 5)
    time.sleep(0.4)
    s2 = cursor_showing()
    print('③ 注入真实鼠标移动后     : 显示中=%s' % s2)

    subprocess.run(['taskkill', '/F', '/T', '/PID', str(p.pid)],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(1.5)
    nudge(7, -3)
    time.sleep(0.5)
    s3 = cursor_showing()
    print('④ 结束并移动鼠标后       : 显示中=%s（应恢复可见）' % s3)

    ok = base and (not s1) and (not s2) and s3
    print('\n结论：%s' % ('✔ 幕布生效：启动即隐藏，移动不露；结束后指针自动恢复' if ok
                        else '✗ 未达预期'))
    return 0 if ok else 2


if __name__ == '__main__':
    sys.exit(main())
