# -*- coding: utf-8 -*-
"""
量化"指针幕布"对画面的影响。

做法：先摆一个纯色参考窗口（底色已知），屏幕抓取它的像素；再挂上幕布，抓同一个矩形；
      比较两次像素差异。alpha=1/255 理论上最多让每个通道降 1 级。
"""
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import sys
import time

u32 = ctypes.windll.user32
gdi = ctypes.windll.gdi32
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCR = os.path.join(ROOT, '屏保', 'TimeCompass.scr')

WS_POPUP, WS_VISIBLE = 0x80000000, 0x10000000
WS_EX_TOPMOST, WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE = 0x8, 0x80, 0x08000000
SRCCOPY = 0x00CC0020
DIB_RGB_COLORS = 0
COLOR = (176, 104, 48)          # 参考窗口底色 (R,G,B)
WX, WY, WW, WH = 380, 300, 620, 400     # 参考窗口位置与大小
CAP_W, CAP_H = 160, 120                 # 抓取区域（窗口中央一块）
CAP_X, CAP_Y = WX + WW // 2 - CAP_W // 2, WY + WH // 2 - CAP_H // 2

WNDPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM)


class WNDCLASSEX(ctypes.Structure):
    _fields_ = [('cbSize', wt.UINT), ('style', wt.UINT), ('lpfnWndProc', WNDPROC),
                ('cbClsExtra', ctypes.c_int), ('cbWndExtra', ctypes.c_int),
                ('hInstance', wt.HINSTANCE), ('hIcon', wt.HICON), ('hCursor', wt.HANDLE),
                ('hbrBackground', wt.HBRUSH), ('lpszMenuName', wt.LPCWSTR),
                ('lpszClassName', wt.LPCWSTR), ('hIconSm', wt.HICON)]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [('biSize', wt.DWORD), ('biWidth', wt.LONG), ('biHeight', wt.LONG),
                ('biPlanes', wt.WORD), ('biBitCount', wt.WORD), ('biCompression', wt.DWORD),
                ('biSizeImage', wt.DWORD), ('biXPelsPerMeter', wt.LONG),
                ('biYPelsPerMeter', wt.LONG), ('biClrUsed', wt.DWORD), ('biClrImportant', wt.DWORD)]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [('bmiHeader', BITMAPINFOHEADER), ('bmiColors', wt.DWORD * 3)]


u32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEX)]
u32.CreateWindowExW.restype = wt.HWND
u32.DefWindowProcW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
u32.DefWindowProcW.restype = ctypes.c_ssize_t
u32.GetDC.restype = wt.HDC
u32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
gdi.CreateSolidBrush.restype = wt.HBRUSH
gdi.CreateCompatibleDC.restype = wt.HDC
gdi.CreateCompatibleBitmap.restype = wt.HBITMAP
gdi.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]
gdi.SelectObject.restype = wt.HGDIOBJ
gdi.BitBlt.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                       wt.HDC, ctypes.c_int, ctypes.c_int, wt.DWORD]
gdi.BitBlt.restype = wt.BOOL
gdi.DeleteObject.argtypes = [wt.HGDIOBJ]
gdi.DeleteDC.argtypes = [wt.HDC]
gdi.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p,
                          ctypes.POINTER(BITMAPINFO), wt.UINT]
gdi.GetDIBits.restype = ctypes.c_int
PROC = WNDPROC(lambda h, m, w, l: u32.DefWindowProcW(h, m, w, l))


def grab(x, y, w, h):
    hdc = u32.GetDC(None)
    mem = gdi.CreateCompatibleDC(hdc)
    bmp = gdi.CreateCompatibleBitmap(hdc, w, h)
    gdi.SelectObject(mem, bmp)
    gdi.BitBlt(mem, 0, 0, w, h, hdc, x, y, SRCCOPY)
    bi = BITMAPINFO()
    bi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bi.bmiHeader.biWidth = w
    bi.bmiHeader.biHeight = -h
    bi.bmiHeader.biPlanes = 1
    bi.bmiHeader.biBitCount = 32
    buf = (ctypes.c_ubyte * (w * h * 4))()
    gdi.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bi), DIB_RGB_COLORS)
    gdi.DeleteObject(bmp)
    gdi.DeleteDC(mem)
    u32.ReleaseDC(None, hdc)
    return buf


def diff(a, b):
    mx = 0
    tot = 0
    n = 0
    chan_max = [0, 0, 0]
    for i in range(0, len(a), 4):
        for c in range(3):
            d = abs(a[i + c] - b[i + c])
            if d:
                n += 1
                tot += d
                if d > chan_max[c]:
                    chan_max[c] = d
                if d > mx:
                    mx = d
    return mx, chan_max, n, (tot / n if n else 0.0), len(a) // 4


def main():
    wc = WNDCLASSEX()
    wc.cbSize = ctypes.sizeof(WNDCLASSEX)
    wc.lpfnWndProc = PROC
    wc.hInstance = ctypes.windll.kernel32.GetModuleHandleW(None)
    wc.lpszClassName = 'TCCurVisRef'
    wc.hbrBackground = gdi.CreateSolidBrush(COLOR[0] | (COLOR[1] << 8) | (COLOR[2] << 16))
    u32.RegisterClassExW(ctypes.byref(wc))
    hwnd = u32.CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                               'TCCurVisRef', '', WS_POPUP | WS_VISIBLE,
                               WX, WY, WW, WH, None, None, wc.hInstance, None)
    print('参考窗口 hwnd=%s 底色 RGB%s  @ (%d,%d) %dx%d' % (hwnd, COLOR, WX, WY, WW, WH))
    time.sleep(1.0)
    a = grab(CAP_X, CAP_Y, CAP_W, CAP_H)
    stat_a = list(a)
    print('抓取区域 (%d,%d) %dx%d；幕布前首像素 RGB=(%d,%d,%d)'
          % (CAP_X, CAP_Y, CAP_W, CAP_H, a[0], a[1], a[2]))

    p = subprocess.Popen([SCR, '/curtain', '8'], cwd=os.path.dirname(SCR))
    try:
        time.sleep(2.0)
        b = grab(CAP_X, CAP_Y, CAP_W, CAP_H)
        print('幕布后首像素 RGB=(%d,%d,%d)' % (b[0], b[1], b[2]))
        mx, chan_max, changed, avg, total = diff(stat_a, b)
        print('\n差异统计（%d 个像素）:' % total)
        print('  最大通道差 = %d 级' % mx)
        print('  分通道最大差 R/G/B = %s' % chan_max)
        print('  有差异的像素 = %d 个 (%.2f%%)，平均差 %.3f 级'
              % (changed, changed * 100.0 / total, avg))
        ok = mx <= 1
        print('\n结论：%s' % ('✔ 幕布对画面影响 ≤1 级（肉眼不可见）' if ok else '✗ 画面被明显改变'))
    finally:
        subprocess.run(['taskkill', '/F', '/T', '/PID', str(p.pid)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        u32.DestroyWindow(hwnd)
        time.sleep(0.3)
    return 0 if mx <= 1 else 2


if __name__ == '__main__':
    sys.exit(main())
