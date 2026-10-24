# -*- coding: utf-8 -*-
"""
单纯观察：Edge kiosk（不经过屏保壳）能存活多久？
用来判断"壳自己退出"到底是 Edge 死了，还是壳的判断出了问题。
"""
import ctypes
import ctypes.wintypes as wt
import os
import shutil
import subprocess
import sys
import time

u32 = ctypes.windll.user32
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TMP = os.path.join(os.environ.get('TEMP', '.'), 'tckiosk')
SECS = int(sys.argv[1]) if len(sys.argv) > 1 else 60


def find_kiosk_windows():
    out = []
    CB = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def cb(h, l):
        if not u32.IsWindowVisible(h):
            return True
        rc = wt.RECT()
        u32.GetWindowRect(h, ctypes.byref(rc))
        cls = ctypes.create_unicode_buffer(64)
        u32.GetClassNameW(h, cls, 64)
        if cls.value == 'Chrome_WidgetWin_1':
            pid = wt.DWORD(0)
            u32.GetWindowThreadProcessId(h, ctypes.byref(pid))
            out.append((h, pid.value, (rc.left, rc.top, rc.right, rc.bottom)))
        return True

    u32.EnumWindows(CB(cb), 0)
    return out


def main():
    os.makedirs(TMP, exist_ok=True)
    src = os.path.join(ROOT, '时间罗盘.html')
    dst = os.path.join(TMP, 'z_clock.html')          # ASCII 名，避免编码问题
    shutil.copyfile(src, dst)
    prof = os.path.join(TMP, 'prof_life')
    shutil.rmtree(prof, ignore_errors=True)
    url = 'file:///' + dst.replace('\\', '/') + '#scr&q=eco'
    args = [EDGE, '--kiosk', url, '--edge-kiosk-type=fullscreen', '--no-first-run',
            '--no-default-browser-check', '--disable-extensions', '--hide-scrollbars',
            '--autoplay-policy=no-user-gesture-required',
            '--user-data-dir=' + prof]
    print('启动 Edge kiosk，观察 %d 秒（不经屏保壳）…' % SECS)
    p = subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    t0 = time.time()
    try:
        while time.time() - t0 < SECS:
            time.sleep(3)
            wins = find_kiosk_windows()
            alive = p.poll() is None
            print('  t=%5.1fs  msedge 进程存活=%-5s  Chrome_WidgetWin 窗口数=%d'
                  % (time.time() - t0, alive, len(wins)))
            if not wins:
                print('  → kiosk 窗口已消失')
                break
    finally:
        subprocess.run(['taskkill', '/F', '/T', '/PID', str(p.pid)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return 0


if __name__ == '__main__':
    sys.exit(main())
