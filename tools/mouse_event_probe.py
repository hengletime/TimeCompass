# -*- coding: utf-8 -*-
"""低级鼠标事件探针：把每一笔 WH_MOUSE_LL 事件原样打印（坐标 + 是否注入）。

用途：屏保壳里"鼠标移动累计 Npx 就退出"，如果 N 在启动瞬间就超过阈值，
说明有程序在注入鼠标事件（或坐标空间在跳变）。本探针用来定位到底是谁、
是不是 LLMHF_INJECTED（SendInput 注入的事件会带这个标记）。

用法：python tools/mouse_event_probe.py [秒数]
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

u32 = ctypes.windll.user32
k32 = ctypes.windll.kernel32

WH_MOUSE_LL = 14
LLMHF_INJECTED = 0x1
LLMHF_LOWER_IL_INJECTED = 0x2


class POINT(ctypes.Structure):
    _fields_ = [('x', wt.LONG), ('y', wt.LONG)]


class MSLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [('pt', POINT), ('mouseData', wt.DWORD), ('flags', wt.DWORD),
                ('time', wt.DWORD), ('extra', ctypes.c_void_p)]


HOOKPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, ctypes.c_int, wt.WPARAM, wt.LPARAM)

u32.SetWindowsHookExW.argtypes = [ctypes.c_int, HOOKPROC, wt.HINSTANCE, wt.DWORD]
u32.SetWindowsHookExW.restype = ctypes.c_void_p
u32.CallNextHookEx.argtypes = [ctypes.c_void_p, ctypes.c_int, wt.WPARAM, wt.LPARAM]
u32.CallNextHookEx.restype = ctypes.c_ssize_t
u32.UnhookWindowsHookEx.argtypes = [ctypes.c_void_p]
u32.GetCursorPos.argtypes = [ctypes.POINTER(POINT)]
k32.GetModuleHandleW.restype = ctypes.c_void_p
k32.GetModuleHandleW.argtypes = [wt.LPCWSTR]

T0 = time.time()
ROWS = []
DOWNS = []
prev = None


def on_event(nCode, wParam, lParam):
    global prev
    if nCode >= 0:
        ms = ctypes.cast(lParam, ctypes.POINTER(MSLLHOOKSTRUCT)).contents
        t = time.time() - T0
        if prev is None:
            d = 0
        else:
            d = abs(ms.pt.x - prev[0]) + abs(ms.pt.y - prev[1])
        prev = (ms.pt.x, ms.pt.y)
        ROWS.append((t, ms.pt.x, ms.pt.y, ms.flags, d))
        if wParam != 0x0200:                      # 非 WM_MOUSEMOVE
            DOWNS.append((t, hex(wParam), ms.flags))
    return u32.CallNextHookEx(None, nCode, wParam, lParam)


HOOK = HOOKPROC(on_event)


def main():
    secs = int(sys.argv[1]) if len(sys.argv) > 1 else 12
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
        dpi_note = 'per-monitor aware'
    except Exception:
        dpi_note = 'DPI unaware'
    p = POINT()
    u32.GetCursorPos(ctypes.byref(p))
    print('探针进程 DPI: %s | 当前指针=(%d,%d)' % (dpi_note, p.x, p.y))

    h = u32.SetWindowsHookExW(WH_MOUSE_LL, HOOK, k32.GetModuleHandleW(None), 0)
    print('低级鼠标钩子 =', h)
    if not h:
        print('钩子安装失败，err=', k32.GetLastError())
        return 1

    msg = wt.MSG() if hasattr(wt, 'MSG') else None
    print('监听 %d 秒（请先别碰鼠标）…' % secs)
    end = time.time() + secs
    while time.time() < end:
        time.sleep(0.05)
    u32.UnhookWindowsHookEx(h)

    print('\n共收到 %d 笔鼠标消息，其中非移动 %d 笔' % (len(ROWS), len(DOWNS)))
    print('%-8s %-7s %-7s %-9s %-8s %s' % ('t(s)', 'x', 'y', 'flags', 'Δpx', '判读'))
    total = 0
    for t, x, y, fl, d in ROWS[:80]:
        total += d
        inj = []
        if fl & LLMHF_INJECTED:
            inj.append('INJECTED')
        if fl & LLMHF_LOWER_IL_INJECTED:
            inj.append('LOWER_IL')
        tag = ','.join(inj) if inj else ('移动' if d else '首笔')
        print('%-8.3f %-7d %-7d 0x%08X %-8d %s' % (t, x, y, fl, d, tag))
    if len(ROWS) > 80:
        print('…（共 %d 笔，只列前 80）' % len(ROWS))
    print('\n累计位移 = %d px（壳的退出阈值是 >30px）' % total)
    n_inj = sum(1 for r in ROWS if r[3] & LLMHF_INJECTED)
    print('其中标记为"注入(SendInput)"的 = %d 笔 / %d 笔' % (n_inj, len(ROWS)))
    if DOWNS:
        print('非移动消息明细：', DOWNS[:10])
    return 0


if __name__ == '__main__':
    sys.exit(main())
