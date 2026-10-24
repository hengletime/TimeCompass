# -*- coding: utf-8 -*-
"""HTTP 零写入模式实测：/s /http 启动 → 从 diag 拿端口 → GET 验证 HTML →
   确认全屏 Edge 窗口 → 注入鼠标退出 → 确认清理。"""
import ctypes, ctypes.wintypes as wt, os, re, socket, subprocess, sys, time

SCR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "屏保", "TimeCompass.scr")
DIAG = os.path.join(os.environ.get("TEMP", ""), "tc_diag.log")
u32 = ctypes.windll.user32

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    pass

class RECT(ctypes.Structure):
    _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long),
                ("r", ctypes.c_long), ("b", ctypes.c_long)]

def full_windows(pids):
    out = []
    EB = ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(RECT), ctypes.c_void_p)
    u32.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(wt.DWORD)]
    def cb(h, _, rc, __):
        pid = wt.DWORD()
        u32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        if pid.value in pids and u32.IsWindowVisible(h):
            r = rc.contents
            if (r.r - r.l) >= 2800 and (r.b - r.t) >= 1700:   # 近全屏
                out.append(h)
        return 1
    u32.EnumWindows(EB(cb), None)
    return out

def msedge_pids():
    p = subprocess.run(["tasklist", "/FI", "IMAGENAME eq msedge.exe", "/FO", "CSV"],
                       capture_output=True, text=True,
                       encoding="gbk", errors="replace")   # tasklist 输出是 GBK
    return set(int(m) for m in re.findall(r'"msedge\.exe","(\d+)"', p.stdout or ""))

def get_diag():
    try:
        with open(DIAG, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""

def kill_screen():
    subprocess.run(["taskkill", "/F", "/IM", "TimeCompass.scr", "/T"],
                   capture_output=True)

ok = True
try:
    if os.path.exists(DIAG):
        os.remove(DIAG)
    print("① 启动 /s /http /diag …")
    p = subprocess.Popen([SCR, "/s", "/http", "/diag"], cwd=os.path.dirname(SCR))
    time.sleep(6)
    diag = get_diag()
    m = re.search(r"HTTP 模式：127\.0\.0\.1:(\d+)", diag)
    if not m:
        print("   ✗ 诊断日志里没有 HTTP 端口：")
        print(diag[-600:])
        sys.exit(2)
    port = int(m.group(1))
    print("   端口 = %d" % port)

    print("② GET http://127.0.0.1:%d/ 验证内容 …" % port)
    s = socket.create_connection(("127.0.0.1", port), timeout=3)
    s.sendall(b"GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n")
    data = b""
    while True:
        chunk = s.recv(65536)
        if not chunk:
            break
        data += chunk
    s.close()
    head_ok = data.startswith(b"HTTP/1.1 200 OK")
    body = data.split(b"\r\n\r\n", 1)[1] if b"\r\n\r\n" in data else b""
    body_ok = body.lstrip().startswith(b"<!DOCTYPE") and b"RING_DEFS" in body
    print("   HTTP 200=%s  HTML完整(<!DOCTYPE+RING_DEFS)=%s  字节=%d"
          % (head_ok, body_ok, len(body)))
    ok = ok and head_ok and body_ok

    print("③ favicon 请求 → 404 …")
    s = socket.create_connection(("127.0.0.1", port), timeout=3)
    s.sendall(b"GET /favicon.ico HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n")
    r = s.recv(128)
    s.close()
    f404 = r.startswith(b"HTTP/1.1 404")
    print("   404=%s" % f404)
    ok = ok and f404

    print("④ 全屏 Edge 窗口 …")
    time.sleep(4)
    fw = full_windows(msedge_pids())
    print("   全屏 Edge 窗口 = %d（应 ≥1）" % len(fw))
    ok = ok and len(fw) >= 1

    print("⑤ 注入鼠标移动退出 …")
    INP = type("INP", (), {})
    class MOUSEINPUT(ctypes.Structure):
        _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
                    ("dwFlags", wt.DWORD), ("time", wt.DWORD),
                    ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]
    class INPUT(ctypes.Structure):
        class U(ctypes.Union):
            _fields_ = [("mi", MOUSEINPUT), ("pad", ctypes.c_byte * 32)]
        _anonymous_ = ("u",)
        _fields_ = [("type", wt.DWORD), ("u", U)]
    for dx in (40, 40, 40, 40):
        i = INPUT()
        i.type = 0
        i.mi = MOUSEINPUT(dx, 8, 0, 0x0001, 0, None)
        u32.SendInput(1, ctypes.byref(i), ctypes.sizeof(INPUT))
        time.sleep(0.15)
    time.sleep(3)
    alive = p.poll() is None
    print("   壳存活=%s（应 False）" % alive)
    ok = ok and not alive
finally:
    kill_screen()
    time.sleep(1.5)
    left = full_windows(msedge_pids())
    print("⑥ 清理后全屏 Edge 窗口 = %d（应 0）" % len(left))
    ok = ok and len(left) == 0

print("\n结论：%s" % ("✔ HTTP 零写入模式全链路可用" if ok else "✗ 有未通过项，见上"))
sys.exit(0 if ok else 2)
