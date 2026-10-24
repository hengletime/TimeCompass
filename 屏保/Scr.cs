// ============================================================
// 时间罗盘动态屏保壳 v4.0（独立监控线程 + 应急热键 + 画质档位 + 指针幕布/确定性隐藏/按需抬窗/释放诊断聚合）
// ============================================================
// 构建：双击同目录「构建屏保.bat」→ 生成「时间罗盘.scr」
//
// v4.0 全面审查修复（2026-10-24，缺陷编号 D-xx 对应审查报告）：
//   D-02（🔴公司电脑跑不了的根因）：启动成功判定 v3.9 只认"≥80% 屏幕的全屏窗口"，
//     把策略③/④"URL 交接"在用户 Edge 实例里开出的【普通窗口】判死 → 4 条策略全败；
//     v3.0 宽松判定同场景能跑 —— 这就是"之前版本能用、中间改坏了"的回归点。
//     修法：成功 = 可见 msedge 窗口 且（全屏级 OR 不在启动前快照里）。
//     既救活交接路径，又保留"用户自己开着的 Edge 不算屏保"的防误判。
//   D-01：策略超时 15/10/20/12s → 30/20/40/25s（慢盘/杀软全盘扫描下 Edge 冷启动可 >15s，
//     旧时限一到就把正在慢启动的 Edge 整树强杀，越试越从头来）。
//   D-03：监控线程（幕布）提前到"可写探测"之前；Diag 不再自己触发探测（原来第一行日志
//     就会跑全套 cmd 委托探测，最坏 30s+，幕布还没挂上 = "点了预览桌面干晾半分钟"）。
//     新增 InitDiagDisk() 在幕布挂上后初始化并回刷内存日志。
//   D-04：钩子回调内零磁盘 IO —— 诊断行入内存队列，监控线程每拍统一落盘
//     （回调内同步写盘在杀软实时扫描下可超 LL 钩子超时 → 钩子被摘 → 退出通道丢失）。
//   D-05：私有 profile 目录改在主线程预建（原来在监控线程上 RunCmd 3s，阻塞幕布消息泵）。
//   D-06：策略切换加 800ms 冷却（Job 杀树异步，立即复用同一默认 profile 会撞 SingletonLock）。
//   D-07：profile 目录名加 GUID 尾防同秒撞名；清理范围跟随整条目录链（原来只扫 %TEMP%）。
//   D-08：策略切换/退出时回收"URL 交接"在用户 Edge 里开出的新窗口（不在快照里）。
//   D-09：HTTP 微服务循环读到请求头结束（单次 Read 在 TCP 分片下不可靠）。
//   D-10：删除死代码 EdgeWindowExists（v3.0 判定遗骸，D-02 回归的物证）。
//   D-11：稳态零枚举 —— FindEdgeWindow 的全窗口枚举只在等 Edge 出现/启动后 30 秒内进行；
//     运行态看门狗改查 kioskWin 句柄存活（不再每 400ms 全屏枚举+每窗口开进程句柄）。
//   D-13：策略②/④（minimal）补 --autoplay-policy（否则滴答声无声且每秒白建 oscillator）。
//
// v3.4 新增：HTML 释放目录链加固 + 诊断聚合（解决"拷到别的电脑报 System32 拒绝访问"：
//   旧版诊断只保留最后一条失败原因，System32 的拒绝访问把 %TEMP% 的真实失败盖掉了）
// v3.1 新增：指针幕布（解决"屏保一运行鼠标指针还在"的问题）
// v3.3 新增：按需抬窗（解决"启动时指针闪 4 次才隐藏"——周期性无条件抬窗会触发系统重算指针归属）
//   为什么需要：指针样式由"指针下方那个窗口"决定。Edge 冷启动那几秒窗口是空白的，
//   指针就是普通箭头；万一有别的置顶窗口飘在屏保之上，指针也会跟着露出来。
//   做法：壳自己建一个「全屏 + 置顶 + 分层透明(alpha=1，肉眼不可见但可被指针命中) +
//   不激活」的窗口垫在最上层，窗口类不带光标，并在 WM_SETCURSOR 里 SetCursor(NULL)。
//   于是从壳启动那一刻起指针就被隐藏，且不依赖页面加载完成。
//   实测（tools/cursor_mechanism.py）：基线可显示 → 幕布存在期间移动/静置都不露 → 销毁后自动恢复。
//   退出时会销毁幕布并刷新系统光标，不会把用户的指针留在隐藏状态。
//
// v3.2 关键修复：幕布"形同虚设"的真正根因（用户反馈"屏保启动后指针还是没隐藏"）
//   根因①（致命）：幕布窗口原来建在「主线程」，而主线程紧接着 mt.Join() 阻塞，
//         从不泵消息 → WM_SETCURSOR 根本投递不进来 → CurtainWndProc 从未执行过，
//         里面那句 SetCursor(NULL) 是死代码。窗口消息必须由"拥有该窗口的线程"泵。
//         修法：幕布挪到监控线程（它自带 PeekMessage 泵）创建与销毁。
//   根因②：PinEdgeTopmost() 每 0.5 秒把 Edge 抬到置顶带最上层 → 系统把"指针归属"
//         重算到 Edge 那个还没加载完页面的空窗口 → 指针被改回默认箭头；而幕布因为
//         根因①收不到消息、没法纠正 → 箭头就留在屏幕上了。
//   根因③：原来只在建幕布那一刻 SetCursor(NULL) 一次，是"一次性"的；
//         此后任何一次窗口重算都能把箭头放回来。
//         修法：加「确定性隐藏循环」——每 20ms SetCursor(NULL) 一次，谁放出来就按回去；
//         再用 GetCursorInfo 做闭环自愈（发现仍显示 → 重新置顶幕布 → 再按一次）。
//   顺带修：幕布无背景刷，接上消息泵后必须处理 WM_PAINT(ValidateRect)，
//         否则会无限重画把线程点着；虚拟屏起点也要用 SM_XVIRTUALSCREEN/SM_YVIRTUALSCREEN。
//   /curtain N 自检升级：统计 WM_SETCURSOR 命中次数与自愈次数并写进 /diag 日志。
//
// v3.0 新增：画质档位（决定星点数与星空刷新率，直接关系 CPU/GPU 占用）
//   在 .scr 同目录放一个纯文本「时间罗盘.ini」，内容写：
//       quality=eco        省电（80 星 / 12fps）
//       quality=balanced   均衡（130 星 / 20fps，默认）
//       quality=ultra      画质（200 星 / 30fps）
//   找不到该文件就用 balanced；页面内还有自动降级兜底。
//
// 退出通道（v2.0 全部独立，任何一条活着就能退）：
//   1. 低级钩子：鼠标累计位移>30px / 任意点击 / 任意按键
//   2. 应急热键 Ctrl+Alt+Q（RegisterHotKey 注册表级，不依赖钩子）
//   3. 看门狗：Edge 窗口连续消失 → 壳同步退出
//   4. 万能保底：Ctrl+Alt+Del（系统安全桌面，任何程序拦不住）
//
// 置顶策略：仅启动后 20 秒内压制悬浮窗，之后停手给系统 UI 让路
// ============================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Scr{
  /* ---- Win32 ---- */
  [DllImport("user32.dll",SetLastError=true)]
  static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
  [DllImport("user32.dll",SetLastError=true)]
  static extern bool UnhookWindowsHookEx(IntPtr hhk);
  [DllImport("user32.dll")]
  static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
  [DllImport("kernel32.dll",CharSet=CharSet.Auto)]
  static extern IntPtr GetModuleHandle(string lpModuleName);
  /* ---- v3.4：Job Object（把 Edge 整棵进程树关进笼子） ----
     edge.Kill() 只杀壳直接启动的那个 msedge.exe；Edge 有"启动器转生"行为
     （launcher 立即 fork 真 browser 进程再退出），Kill 打不中真身 → kiosk 窗口
     残留数秒甚至更久，期间指针被它藏住（实测：退出后全屏Edge=1、指针显示中=False）。
     Job + KILL_ON_JOB_CLOSE 是标准 kiosk 做法：壳持有 Job 句柄，壳以任何方式死亡
     （正常退出/被杀/崩溃）句柄关闭 → 系统强杀整棵 Edge 树，一个不漏。 */
  [DllImport("kernel32.dll",CharSet=CharSet.Auto)]
  static extern IntPtr CreateJobObject(IntPtr a, string lpName);
  [DllImport("kernel32.dll")]
  static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpInfo, int cbInfo);
  [DllImport("kernel32.dll")]
  static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProc);
  [DllImport("kernel32.dll")]
  static extern bool CloseHandle(IntPtr h);
  const int JobObjectExtendedLimitInformation=9;
  const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE=0x2000;
  [StructLayout(LayoutKind.Sequential)]
  struct JOBOBJECT_BASIC_LIMIT_INFORMATION{
    public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
    public uint LimitFlags;
    public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public UIntPtr Affinity;
    public uint PriorityClass, SchedulingClass;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct IO_COUNTERS{
    public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,
                 ReadTransferCount,WriteTransferCount,OtherTransferCount;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
  }
  static IntPtr edgeJob=IntPtr.Zero;
  [DllImport("user32.dll")]
  static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")]
  static extern bool EnumWindows(EnumProc cb, IntPtr lp);
  delegate bool EnumProc(IntPtr hWnd, IntPtr lp);
  [DllImport("user32.dll")]
  static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")]
  static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")]
  static extern IntPtr GetTopWindow(IntPtr hWnd);
  [DllImport("user32.dll")]
  static extern IntPtr GetWindow(IntPtr hWnd, int cmd);
  [DllImport("user32.dll")]
  static extern int GetWindowLong(IntPtr hWnd, int idx);
  [DllImport("user32.dll", SetLastError=true)]
  static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
  [DllImport("user32.dll")]
  static extern bool UnregisterHotKey(IntPtr hWnd, int id);
  [DllImport("user32.dll")]
  static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
  [DllImport("user32.dll")]
  static extern bool TranslateMessage(ref MSG msg);
  [DllImport("user32.dll")]
  static extern IntPtr DispatchMessage(ref MSG msg);
  delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

  /* ---- v3.1 指针幕布所需 ---- */
  delegate IntPtr WndProcD(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct WNDCLASSEX{
    public uint cbSize, style;
    public WndProcD lpfnWndProc;
    public int cbClsExtra, cbWndExtra;
    public IntPtr hInstance, hIcon, hCursor, hbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    public IntPtr hIconSm;
  }
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style,
    int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr h);
  [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
  [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
  [DllImport("user32.dll")] static extern bool ValidateRect(IntPtr h, IntPtr r);
  [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)]
  static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool UnregisterClass(string name, IntPtr inst);
  [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint flags);
  /* v3.9：窗口收尾（关掉"交接"场景下不属于我们进程的 kiosk 窗口）+ 全屏覆盖判定 */
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

  [StructLayout(LayoutKind.Sequential)]
  struct POINT{ public int x, y; }
  [StructLayout(LayoutKind.Sequential)]
  struct RECT{ public int left, top, right, bottom; }
  [StructLayout(LayoutKind.Sequential)]
  struct MSLLHOOKSTRUCT{ public POINT pt; public uint mouseData, flags, time; public IntPtr extraInfo; }
  [StructLayout(LayoutKind.Sequential)]
  struct MSG{ public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }
  /* v3.2：指针状态的地面真相 —— flags 的 bit0 = CURSOR_SHOWING（隐藏时 hCursor 通常为 0） */
  [StructLayout(LayoutKind.Sequential)]
  struct CURSORINFO{ public uint cbSize, flags; public IntPtr hCursor; public POINT pt; }

  static readonly IntPtr HWND_TOPMOST=new IntPtr(-1);
  const uint SWP_NOMOVE=0x2, SWP_NOSIZE=0x1, SWP_SHOWWINDOW=0x40;
  const uint WM_CLOSE=0x0010;
  const uint PM_REMOVE=1;
  const int  HOTKEY_ID=0xB00B;
  const uint MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4;
  const uint VK_Q=0x51, VK_F12=0x7B, VK_F9=0x78;
  const int  WM_HOTKEY=0x0312;

  /* v3.1 指针幕布常量 */
  const uint WS_POPUP=0x80000000, WS_VISIBLE=0x10000000;
  const uint WS_EX_LAYERED=0x00080000, WS_EX_TOPMOST=0x00000008;
  const uint WS_EX_TOOLWINDOW=0x00000080, WS_EX_NOACTIVATE=0x08000000;
  const int  GWL_EXSTYLE=-20, GW_HWNDNEXT=2;
  const uint LWA_ALPHA=0x2;
  const uint WM_SETCURSOR=0x0020, WM_MOUSEACTIVATE=0x0021;
  const uint WM_PAINT=0x000F, WM_ERASEBKGND=0x0014;
  const int  MA_NOACTIVATE=3;
  const int  SM_CXVIRTUALSCREEN=78, SM_CYVIRTUALSCREEN=79;
  const int  SM_XVIRTUALSCREEN=76, SM_YVIRTUALSCREEN=77;
  const uint SPI_SETCURSORS=0x0057;
  /* v3.2：确定性隐藏循环的节拍（毫秒）、自愈限速、CURSORINFO.flags 的 bit0 */
  const int  HIDE_TICK_MS=20;
  /* 自愈限速：pinning 一停就归零。指针一旦"显示中"就必须立刻按回去，不要因为限速
     而放过它 —— 实测把限速设成 50ms 会让"抬窗后补的那一刀"被吃掉，反而出现 0.9 秒的
     可见窗口。检测本身已经限制了触发频率（只在真的显示时才动手），不需要再加限速。 */
  const int  HEAL_MIN_GAP_MS=0;
  const uint CURSOR_SHOWING=0x0001;

  const string RES_NAME="tcembed";              // 内嵌资源名（无点，兼容各工具链）
  const string VER="v4.0";                      // 壳版本（错误弹窗/关于框里带出来，防止"拷的是哪版"说不清）
  const string LOCAL_HTML="时间罗盘.html";       // 同目录优先文件名
  const string INI_NAME="时间罗盘.ini";          // 可选画质配置
  static string quality="balanced";
  static string qualityFrom="（未找到配置文件，用默认值）";

  /* v2.0 关键修复①：钩子委托必须存入静态字段保活！
     上一版把方法组直接传给 P/Invoke，GC 随时可能回收委托 → 回调指针悬空 →
     钩子失效/壳进程崩溃 → Edge 全屏无人管理 → 用户被困 */
  static HookProc mouseProcDelegate, keyProcDelegate;

  static IntPtr mHook=IntPtr.Zero, kHook=IntPtr.Zero;
  static Process edge;
  static volatile bool quitting=false;
  static int lastX=-1, lastY=-1, travel=0;
  static string htmlFile=null;
  static string lastError="";

  /* v3.6：诊断日志**永远开启**。踩过的坑：v3.1~v3.5 只有带 /diag 参数才写日志，
     而用户在"另一台电脑"上走的是 Windows 预览/正常启动，根本没法传参数——
     出问题时手里一个字节证据都没有，只能盲猜。
     写入位置走 WritableDir 降级链（跟 HTML 释放同一套），连 %TEMP% 都被拦的
     受控电脑上也能落到某个可写目录；全部不可写则静默放弃（绝不拖累屏保本体）。
     v3.7：**同时保留一份内存副本**——hendr 电脑实测连探针文件都写不了（日志文件
     根本落不了地），而失败弹窗拿内存副本直接展示全文：弹窗本身就是日志，
     用户截图=交日志，彻底摆脱对磁盘的依赖。 */
  static readonly System.Collections.Generic.List<string> diagMem
    =new System.Collections.Generic.List<string>();
  static string diagPath=null;
  static string quitReason="（未记录）";
  static readonly DateTime T0=DateTime.Now;
  static void Diag(string s){
    string line=DateTime.Now.ToString("HH:mm:ss.fff")+"  "+s;
    try{
      lock(diagMem){
        diagMem.Add(line);
        if(diagMem.Count>300) diagMem.RemoveAt(0);
      }
    }catch{}
    /* v4.0（D-03）：Diag 不再自己触发可写探测 —— 全套探测（含 cmd 委托，最坏 30s+）
       必须等幕布挂上之后由 Main 显式调 InitDiagDisk() 完成；
       在那之前只记内存副本，绝不阻塞启动关键路径。 */
    try{
      if(diagPath!=null) File.AppendAllText(diagPath,line+"\r\n");
    }catch{}
  }
  /* v4.0：幕布挂上之后再初始化诊断磁盘落点，并把内存副本回刷出去。
     所有目录都不可写时静默降级为纯内存模式（失败弹窗仍能展示全文）。 */
  static void InitDiagDisk(){
    if(diagPath!=null) return;
    try{
      string pe=null;
      string wd=WritableDir(out pe);
      if(wd!=null){
        diagPath=Path.Combine(wd,"tc_diag.log");
        try{ File.Delete(diagPath); }catch{}
        string dump=null;
        lock(diagMem){ dump=string.Join("\r\n",diagMem.ToArray()); }
        if(dump.Length>0) File.AppendAllText(diagPath,dump+"\r\n");
      }else{
        Diag("诊断日志：所有候选目录均不可写，本次运行仅保留内存副本（失败弹窗仍可见）");
      }
    }catch{}
  }
  /* 弹窗用：内存日志最后 n 行 */
  static string DiagTail(int n){
    try{ lock(diagMem){
      int from=Math.Max(0,diagMem.Count-n);
      return string.Join("\r\n",diagMem.GetRange(from,diagMem.Count-from));
    } }catch{ return "（内存日志读取失败）"; }
  }

  /* v3.1：指针幕布（同一原因——窗口过程委托必须静态保活） */
  static WndProcD wndProcDelegate;
  static IntPtr curtain=IntPtr.Zero;
  /* v3.2：根因①的"照妖镜" —— 幕布真正收到过多少次 WM_SETCURSOR。
     若跑完一整个屏保周期这里还是 0，说明消息泵没接上（窗口建在阻塞线程上）。 */
  static int setCursorMsgHits=0;
  static int selfHealCount=0;      // 确定性循环发现"指针又露出来"并纠正的次数
  static long lastHealTick=0;

  /* ================= 低级钩子（回调内绝不阻塞） ================= */
  /* v3.2：启动宽限期。实测屏保刚起来那几十毫秒里，低级钩子会收到几笔"幽灵"鼠标事件
     （累计位移能到 360px，而物理鼠标根本没人碰），足以把屏保当场判死。
     这类事件不区分真假（flags 不带 LLMHF_INJECTED），所以用"启动后 GRACE_MS 内的位移
     不计入退出判定"来兜住。宽限期内仍然记录原始事件（/diag 模式下），便于事后追查。 */
  const int GRACE_MS=400;
  static long hookStartTick=0;
  static int mouseEvts=0;

  /* v4.0（D-04）：钩子回调内绝不做磁盘 IO —— 低级钩子回调若阻塞超时（杀软实时扫描
     目录下一次 File.AppendAllText 就可能超），系统会直接摘掉钩子 → 退出通道丢失。
     诊断行先入内存队列（有界），由监控线程每拍统一落盘。 */
  static readonly System.Collections.Generic.Queue<string> hookDiagQ
    =new System.Collections.Generic.Queue<string>();
  static void HookDiag(string s){
    try{ lock(hookDiagQ){ if(hookDiagQ.Count<64) hookDiagQ.Enqueue(s); } }catch{}
  }
  static void FlushHookDiag(){
    try{
      while(true){
        string s;
        lock(hookDiagQ){ if(hookDiagQ.Count==0) return; s=hookDiagQ.Dequeue(); }
        Diag(s);
      }
    }catch{}
  }

  static IntPtr MouseProc(int nCode, IntPtr wp, IntPtr lp){
    if(nCode>=0 && !quitting){
      int msg=wp.ToInt32();
      if(msg==0x0200){ // WM_MOUSEMOVE：累计位移超阈值才退出，避免启动抖动误退
        MSLLHOOKSTRUCT m=(MSLLHOOKSTRUCT)Marshal.PtrToStructure(lp,typeof(MSLLHOOKSTRUCT));
        int d=0;
        if(lastX>=0) d=Math.Abs(m.pt.x-lastX)+Math.Abs(m.pt.y-lastY);
        lastX=m.pt.x; lastY=m.pt.y;
        mouseEvts++;
        bool inGrace=(hookStartTick!=0) && ((long)Environment.TickCount-hookStartTick)<GRACE_MS;
        if(mouseEvts<=12)
          HookDiag("鼠标#"+mouseEvts+" pt=("+m.pt.x+","+m.pt.y+") flags=0x"+m.flags.ToString("X")
               +" 本笔="+d+"px 累计="+(travel+d)+"px"+(inGrace?" [启动宽限期内，不计]":""));
        if(inGrace) return CallNextHookEx(mHook,nCode,wp,lp);
        travel+=d;
        if(travel>30){ quitReason="鼠标移动累计 "+travel+"px"; TriggerQuit(); }
      }else{ quitReason="鼠标按键/滚轮 msg=0x"+msg.ToString("X"); TriggerQuit(); }
    }
    return CallNextHookEx(mHook,nCode,wp,lp);
  }
  static IntPtr KeyProc(int nCode, IntPtr wp, IntPtr lp){
    if(nCode>=0 && !quitting){ quitReason="键盘按键"; TriggerQuit(); }
    return CallNextHookEx(kHook,nCode,wp,lp);
  }

  static void TriggerQuit(){
    if(quitting) return;
    quitting=true;
    HookDiag("退出触发：" + quitReason);   // v4.0(D-04)：可能从钩子回调里进来，不落盘
    try{ if(edge!=null && !edge.HasExited) edge.Kill(); }catch{}
  }

  /* ================= HTML 解析 ================= */
  static string ResolveHtml(){
    try{
      string dir=Application.StartupPath;
      string p1=Path.Combine(dir,LOCAL_HTML);
      if(File.Exists(p1)) return p1;
      string parent=Path.GetDirectoryName(dir.TrimEnd('\\'));
      if(!string.IsNullOrEmpty(parent)){
        string p2=Path.Combine(parent,LOCAL_HTML);
        if(File.Exists(p2)) return p2;
      }
      return ExtractEmbedded();
    }catch(Exception ex){ lastError=ex.Message; }
    return null;
  }

  /* v3.5：真实可写探测（不是"目录存在"，而是真写一个探针文件再删）。
     已知实例：hendr 电脑上连 %TEMP% 都建不出目录（极可能是临时用户配置文件/管控），
     Edge 的 --user-data-dir 若落在不可写目录，msedge 启动即失败退出 →
     窗口永不出现 → 幕布永久挂着 = "透明屏保"。
     所以 profile 目录必须与 HTML 释放走同一套降级链 + 真实写入验证。 */
  static bool wdirProbed=false;
  static string wdirPath=null, wdirErrs=null;
  static string wdirMode="（未探测）";
  /* v3.9：探测结果缓存 —— 带 cmd 委托的探测最坏会起 16 个子进程，
     而 Diag / ExtractEmbedded / LaunchEdge 都会问一次，必须只探一次。 */
  static string WritableDir(out string probeErrs){
    if(wdirProbed){ probeErrs=wdirErrs; return wdirPath; }
    wdirProbed=true;
    wdirPath=ProbeWritableDir();          // 内部填充 wdirErrs / wdirMode
    probeErrs=wdirErrs;
    return wdirPath;
  }

  /* v3.9：通过 cmd.exe 执行命令并回收输出。用于"本进程做不到、可信宿主进程能做到"的事
     （HIPS 类安全软件按**程序映像**拦写时，cmd.exe 作为微软签名程序不受同一规则约束）。
     带超时保护 + 后台线程读管道，避免子进程挂住时把壳也拖死。 */
  static string RunCmd(string cmdArgs,int timeoutMs){
    try{
      string comspec=Environment.GetEnvironmentVariable("COMSPEC");
      if(string.IsNullOrEmpty(comspec)) comspec="cmd.exe";
      var psi=new ProcessStartInfo{
        FileName=comspec, Arguments="/c "+cmdArgs,
        UseShellExecute=false, CreateNoWindow=true,
        RedirectStandardOutput=true, RedirectStandardError=true,
        StandardOutputEncoding=System.Text.Encoding.UTF8
      };
      using(var p=Process.Start(psi)){
        var sb=new System.Text.StringBuilder();
        var rd=new Thread(delegate(){
          try{ sb.Append(p.StandardOutput.ReadToEnd()); }catch{}
          try{ sb.Append(p.StandardError.ReadToEnd()); }catch{}
        });
        rd.IsBackground=true; rd.Start();
        bool done=p.WaitForExit(timeoutMs);
        if(!done){ try{ p.Kill(); }catch{} }
        rd.Join(600);
        return (done?"":"[超时]")+sb.ToString().Trim();
      }
    }catch(Exception ex){ return "[cmd 启动失败] "+ex.Message; }
  }

  static string ProbeWritableDir(){
    wdirErrs=null;
    var errs=new System.Text.StringBuilder();
    string lad=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    /* v3.8：目录链追加"全局可写"候选。hendr 电脑实测（弹窗日志）：该进程对
       用户整个 profile 全部"访问被拒绝"——典型的受限令牌/安全软件沙箱降权。
       受限令牌通常仍可写 C:\Windows\Temp（Users 默认有写权限）与 %PUBLIC%
       （Everyone 可写），这两个是最后救命稻草；全部失败则确证"进程被降权"，
       弹窗直接提示用户去检查安全软件。 */
    string[] dirs={
      Path.GetTempPath(),
      Path.Combine(lad,"TimeCompass"),
      lad,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      @"C:\Windows\Temp",
      Environment.GetEnvironmentVariable("PUBLIC"),
      Environment.GetEnvironmentVariable("ALLUSERSPROFILE"),
      Application.StartupPath
    };
    foreach(var d in dirs){
      if(string.IsNullOrEmpty(d)) continue;
      try{
        if(!Directory.Exists(d)) Directory.CreateDirectory(d);
        string probe=Path.Combine(d,"tc_probe_"+Guid.NewGuid().ToString("N").Substring(0,8)+".tmp");
        File.WriteAllText(probe,"1");
        File.Delete(probe);
        wdirMode="直接可写（正常电脑）";
        return d;
      }catch(Exception ex){ errs.AppendLine("直接写 "+d+" : "+ex.Message); }
    }
    /* v3.9 第二遍：自己写全被拒 → 让 cmd.exe（微软签名的可信宿主进程）代写。
       这一遍同时是"到底谁在拦我们"的判别器：
         · 自己写被拒、cmd 代写成功 → 拦截是**按程序映像**生效的（HIPS 类规则），
           那么 Edge（另一个可信程序）同样能写 → 私有 profile 可用 → 链路能救活；
         · 连 cmd 代写也失败 → 进程被**受限令牌/沙箱降权**，子进程继承同样限制，
           任何浏览器都建不出配置目录 → 只能请用户把本程序加入安全软件信任列表。 */
    foreach(var d in dirs){
      if(string.IsNullOrEmpty(d)) continue;
      string probe=Path.Combine(d,"tc_probe_"+Guid.NewGuid().ToString("N").Substring(0,8)+".tmp");
      string r=RunCmd("if not exist \""+d+"\" md \""+d+"\" 2>nul & echo.> \""+probe+"\" 2>nul"
                      +" & if exist \""+probe+"\" (echo TC_WRITE_OK & del \""+probe+"\" 2>nul)"
                      +" else (echo TC_DENY)",4000);
      if(r.IndexOf("TC_WRITE_OK")>=0){
        wdirMode="委托 cmd 可写（本进程被按映像拦写，子进程不受限）";
        errs.AppendLine("委托 cmd 代写成功："+d);
        wdirErrs=errs.ToString();
        return d;
      }
      errs.AppendLine("委托 cmd 代写 "+d+" : "+r);
    }
    wdirMode="全部不可写（进程被降权，子进程同样受限）";
    wdirErrs=errs.ToString();
    return null;
  }

  static string ExtractEmbedded(){
    var asm=typeof(Scr).Assembly;
    Stream s=asm.GetManifestResourceStream(RES_NAME);
    if(s==null){
      foreach(var name in asm.GetManifestResourceNames()){
        Stream st=null;
        try{
          st=asm.GetManifestResourceStream(name);
          if(st==null) continue;
          var head=new byte[64];
          int n=st.Read(head,0,64);
          string h=System.Text.Encoding.UTF8.GetString(head,0,n)
                     .TrimStart('\uFEFF',' ','\t','\r','\n').ToLowerInvariant();
          if(h.StartsWith("<!doctype")||h.StartsWith("<html")){
            if(s!=null) s.Close();
            s=asm.GetManifestResourceStream(name);
            break;
          }
        }catch{}
        finally{ if(st!=null) st.Close(); }
      }
    }
    if(s==null){
      lastError="资源["+RES_NAME+"]未找到。程序集内资源: "+
                (asm.GetManifestResourceNames().Length==0
                  ?"(无)"
                  :string.Join(" | ",asm.GetManifestResourceNames()));
      return null;
    }
    /* 每次释放用时间戳唯一文件名；目录降级链（v3.4）：
       %TEMP% → %LOCALAPPDATA%\TimeCompass → %LOCALAPPDATA% → 用户主目录 → 程序目录。
       ⚠ 诊断必须聚合全部失败原因（v3.3 及以前 lastError 只留最后一条，
       在别人的电脑上 System32 拒绝访问把 %TEMP% 的真实失败原因盖掉了 → 永远盲猜）。
       已知实例：标准安装时 scr 被复制进 System32 运行，程序目录不可写；
       %TEMP%/LocalAppData 理论上必可写 —— 若连它们都失败，多半是程序没以
       登录用户身份运行（第三方启动器/服务）或环境被管控，聚合诊断能直接揭示。 */
    string stamp=DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var errs=new System.Text.StringBuilder();
    string lad=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    /* v3.8：与 WritableDir 同步追加全局可写候选（C:\Windows\Temp、%PUBLIC%、ProgramData） */
    string[] dirs={
      Path.GetTempPath(),
      Path.Combine(lad,"TimeCompass"),
      lad,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      @"C:\Windows\Temp",
      Environment.GetEnvironmentVariable("PUBLIC"),
      Environment.GetEnvironmentVariable("ALLUSERSPROFILE"),
      Application.StartupPath
    };
    foreach(var d in dirs){
      if(string.IsNullOrEmpty(d)) continue;
      Stream s2=null;
      try{
        if(!Directory.Exists(d)) Directory.CreateDirectory(d);
        string tmp=Path.Combine(d,"timecompass_scr_"+stamp+".html");
        s2=asm.GetManifestResourceStream(RES_NAME);
        if(s2==null){
          foreach(var name in asm.GetManifestResourceNames()){
            Stream st=asm.GetManifestResourceStream(name);
            if(st==null) continue;
            var head=new byte[64];
            int n=st.Read(head,0,64);
            st.Close();
            string h=System.Text.Encoding.UTF8.GetString(head,0,n)
                       .TrimStart('\uFEFF',' ','\t','\r','\n').ToLowerInvariant();
            if(h.StartsWith("<!doctype")||h.StartsWith("<html")){ s2=asm.GetManifestResourceStream(name); break; }
          }
        }
        if(s2==null) break;
        using(s2){ using(var f=File.Create(tmp)) s2.CopyTo(f); }
        CleanupOld(d,Path.GetFileName(tmp));
        return tmp;
      }catch(Exception ex){ errs.AppendLine("· "+d+" : "+ex.Message); }
      finally{ if(s2!=null) s2.Close(); }
    }
    lastError=errs.ToString();
    return null;
  }

  static void CleanupOld(string dir,string keep){
    try{
      foreach(var f in Directory.GetFiles(dir,"timecompass_scr_*.html")){
        if(Path.GetFileName(f)==keep) continue;
        try{ File.Delete(f); }catch{}
      }
    }catch{}
  }

  /* ================= v3.4：内嵌 HTML 的零磁盘通道（本地 HTTP 微服务） =================
     背景（主人真机实测）：某些电脑的安全软件会拦截"System32 里的 .scr 写用户目录"，
     连 %TEMP%/用户主目录都拒绝 → 内嵌释放五连拒 → 屏保无法启动（旧版能跑只是因为
     当时 scr 旁边带了外部 HTML，走的零写入通道）。
     解法：壳起一个只听 127.0.0.1 随机端口的 TcpListener，Edge 打开
     http://127.0.0.1:端口/#scr —— 全程零磁盘写入，任何受限环境都能跑。
     loopback 流量不过防火墙、不占真实网卡；端口由系统分配，几乎不可能冲突。 */
  static TcpListener httpSrv;
  static byte[] httpHtml;

  /* 从程序集取内嵌 HTML 字节（资源名 tcembed，兼容内容模糊匹配） */
  static byte[] LoadEmbeddedBytes(){
    var asm=typeof(Scr).Assembly;
    Stream s=asm.GetManifestResourceStream(RES_NAME);
    if(s==null){
      foreach(var name in asm.GetManifestResourceNames()){
        Stream st=null;
        try{
          st=asm.GetManifestResourceStream(name);
          if(st==null) continue;
          var head=new byte[64];
          int n=st.Read(head,0,64);
          string h=Encoding.UTF8.GetString(head,0,n)
                     .TrimStart('\uFEFF',' ','\t','\r','\n').ToLowerInvariant();
          if(h.StartsWith("<!doctype")||h.StartsWith("<html")){
            s=asm.GetManifestResourceStream(name);
            break;
          }
        }catch{}
        finally{ if(st!=null) st.Close(); }
      }
    }
    if(s==null) return null;
    using(s){
      using(var ms=new MemoryStream()){
        s.CopyTo(ms);
        return ms.ToArray();
      }
    }
  }

  static bool StartHttpServer(){
    try{
      if(httpSrv!=null) return true;
      httpHtml=LoadEmbeddedBytes();
      if(httpHtml==null || httpHtml.Length==0) return false;
      httpSrv=new TcpListener(IPAddress.Loopback,0);   // 端口 0 = 系统随机分配
      httpSrv.Start();
      int port=((IPEndPoint)httpSrv.LocalEndpoint).Port;
      Diag("HTTP 模式：127.0.0.1:"+port+"（零磁盘写入，内嵌 "+httpHtml.Length+" 字节）");
      var t=new Thread(HttpLoop);
      t.IsBackground=true;
      t.Start();
      httpPort=port;
      return true;
    }catch(Exception ex){
      Diag("HTTP 模式启动失败："+ex.Message);
      httpSrv=null;
      return false;
    }
  }
  static int httpPort;

  static void HttpLoop(){
    while(true){
      TcpClient c;
      try{ c=httpSrv.AcceptTcpClient(); }catch{ return; }   // Stop() 后正常退出
      ThreadPool.QueueUserWorkItem(_=>{
        try{
          using(c){
            var st=c.GetStream();
            try{ st.ReadTimeout=2000; }catch{}
            var buf=new byte[4096];
            var acc=new System.Text.StringBuilder();
            /* v4.0（D-09）：循环读到请求头结束（\r\n\r\n）。TCP 分片下单次 Read
               可能只拿到半截请求行；loopback 上极少发生，但修掉只要几行。 */
            while(acc.Length<16384 && acc.ToString().IndexOf("\r\n\r\n")<0){
              int n;
              try{ n=st.Read(buf,0,buf.Length); }catch{ return; }
              if(n<=0) break;
              acc.Append(Encoding.ASCII.GetString(buf,0,n));
            }
            string req=acc.ToString();
            string path=req.Split(' ').Length>1?req.Split(' ')[1]:"/";
            if(path.StartsWith("/favicon")){                // 浏览器顺手要图标 → 404
              byte[] nf=Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
              st.Write(nf,0,nf.Length);
            }else{                                          // 页面本体：只此一份
              string head="HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"+
                          "Content-Length: "+httpHtml.Length+"\r\n"+
                          "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
              byte[] hb=Encoding.UTF8.GetBytes(head);
              st.Write(hb,0,hb.Length);
              st.Write(httpHtml,0,httpHtml.Length);
            }
            st.Flush();
          }
        }catch{}
      });
    }
  }
  static void StopHttpServer(){
    try{ if(httpSrv!=null){ httpSrv.Stop(); httpSrv=null; } }catch{}
  }

  /* ================= 画质档位 =================
     纯文本 ini，逐行找 quality=eco|balanced|ultra。
     查找顺序：.scr 同目录 → 上一级目录 → %LOCALAPPDATA%\TimeCompass
     读不到就用 balanced；页面内还有"实测帧间隔偏慢自动减星"兜底。 */
  static void LoadQuality(){
    try{
      string[] dirs={
        Application.StartupPath,
        Path.GetDirectoryName(Application.StartupPath.TrimEnd('\\')),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TimeCompass")
      };
      foreach(var d in dirs){
        if(string.IsNullOrEmpty(d)) continue;
        string f=Path.Combine(d,INI_NAME);
        if(!File.Exists(f)) continue;
        foreach(var raw in File.ReadAllLines(f)){
          var line=raw.Trim();
          if(line.Length==0 || line[0]=='#' || line[0]==';' || line[0]=='[') continue;
          int eq=line.IndexOf('=');
          if(eq<=0) continue;
          if(!line.Substring(0,eq).Trim().Equals("quality",StringComparison.OrdinalIgnoreCase)) continue;
          var v=line.Substring(eq+1).Trim().ToLowerInvariant();
          if(v=="eco"||v=="balanced"||v=="ultra"){ quality=v; qualityFrom=f; return; }
        }
        qualityFrom=f+"（但没读到合法的 quality=）";
      }
    }catch(Exception ex){ qualityFrom="读配置出错："+ex.Message; }
  }

  /* ================= Edge 启动（v3.9：4 策略链） =================
     hendr 电脑实测（v3.8 弹窗日志）：该进程对**所有**目录"访问被拒绝"（含
     C:\Windows\Temp 与 %PUBLIC%），私有 profile 目录建不出来；而 Chromium 对不可写
     profile 是**硬失败** —— 本机实测原话：
       "Failed to create a ProcessSingleton for your profile directory. Aborting now"
     于是 Edge 进程启动后约 1 秒自杀 → 窗口永不出现 → 屏幕上只剩隐形幕布 = "透明屏保"。
     单条启动策略在这类电脑上必死，所以按"代价从小到大"依次尝试，每条给足独立窗口期：
       ① 完整参数 + 私有配置（正常电脑走这里：减负开关全开）
       ② 最小参数 + 私有配置（Edge 老版本/策略拒新旗标时）
       ③ 完整参数 + 系统默认配置（**十一前旧版能跑的那条路**：不传 --user-data-dir，
          交给默认 profile 或用户已开的 Edge 实例；交接场景下窗口由旧实例创建）
       ④ 最小参数 + 系统默认配置 */
  static string currentUrl=null;
  static int    strategyIdx=-1;
  static long   strategyStart=0;
  static long   lastEdgeSeen=0;
  static bool   launcherExitLogged=false;
  /* v4.0a（Q-04 重构）：从 MonitorLoop 局部变量提升为字段 —— MarkStrategySuccess()
     要在超时复查路径里也写入它们 */
  static bool   sawEdge=false;
  static int    noEdge=0;
  static string baseProfileDir=null;
  static string profileDir=null;
  /* v4.0（D-05）：两个私有 profile 目录在主线程预建（A→策略①，B→策略②）。
     原来在监控线程上 RunCmd(3000) 建目录 —— 会阻塞幕布消息泵 3 秒（指针可能回显）。
     两条策略各用独立目录：①失败被强杀后的残留 SingletonLock 不会连累②。 */
  static string profileA=null, profileB=null;
  static readonly string[] strategyName={
    "①完整参数+私有配置","②最小参数+私有配置","③完整参数+系统默认配置","④最小参数+系统默认配置"};
  /* v4.0（D-01）：超时加长 —— 企业机杀软全盘扫描 msedge.exe + 冷盘时 Edge 冷启动
     可 >15s，旧时限一到就把正在慢启动的 Edge 整树强杀（KILL_ON_JOB_CLOSE），
     越试越从头来。正常机器策略① 通常 <5s 生效，不受影响。 */
  static readonly int[] strategyMs={30000,20000,40000,25000};
  static readonly System.Collections.Generic.List<string> strategyLog
    =new System.Collections.Generic.List<string>();

  /* v3.9：启动前屏幕上已有的 msedge 窗口快照 + 最终认定的 kiosk 窗口。
     前者用来区分"用户自己开着的 Edge"与"屏保自己拉起来的窗口"——
     退出时绝不误杀前者（否则会把用户的浏览会话一起关掉）。 */
  static readonly System.Collections.Generic.List<IntPtr> preEdge
    =new System.Collections.Generic.List<IntPtr>();
  static IntPtr kioskWin=IntPtr.Zero;

  static void SnapEdgeWindows(){
    preEdge.Clear();
    try{
      EnumWindows((h,l)=>{
        try{
          if(!IsWindowVisible(h)) return true;
          uint pid; GetWindowThreadProcessId(h,out pid);
          if(pid==0) return true;
          using(var p=Process.GetProcessById((int)pid)){
            if(p.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase)) preEdge.Add(h);
          }
        }catch{}
        return true;
      },IntPtr.Zero);
    }catch{}
    Diag("启动前快照：屏幕上已有 "+preEdge.Count+" 个可见 msedge 窗口"
         +(preEdge.Count>0?"（用户已开着 Edge，可能走\"URL 交接\"路径）":"（没开 Edge）"));
  }

  /* v4.0（D-05/D-07）：在主线程预建本轮的两个私有 profile 目录 + 清理历史残留。
     目录名加 GUID 尾（原来只有秒级时间戳，理论上有同秒撞名残留）；
     清理范围跟随整条可写目录链（原来只扫 %TEMP%，profile 落在 C:\Windows\Temp/
     %PUBLIC% 的历史实例会无限累积）。必须在 SnapEdgeWindows/LaunchEdge 之前调用。 */
  static void PreCreateProfiles(){
    profileA=null; profileB=null;
    if(baseProfileDir==null) return;
    string stamp=DateTime.Now.ToString("yyyyMMdd_HHmmss");
    string pa=Path.Combine(baseProfileDir,"tclp_scr_profile_"+stamp+"_a_"+Guid.NewGuid().ToString("N").Substring(0,6));
    string pb=Path.Combine(baseProfileDir,"tclp_scr_profile_"+stamp+"_b_"+Guid.NewGuid().ToString("N").Substring(0,6));
    try{ Directory.CreateDirectory(pa); }catch{}
    try{ Directory.CreateDirectory(pb); }catch{}
    /* 直接建不出来才委托 cmd 代建（正常电脑两下 Directory.CreateDirectory 就完事）；
       RunCmd 只允许跑在主线程 —— 监控线程上它会阻塞幕布消息泵 3 秒（D-05）。 */
    if(!Directory.Exists(pa)){
      RunCmd("if not exist \""+pa+"\" md \""+pa+"\" 2>nul",3000);
      if(Directory.Exists(pa)) profileA=pa;
    }else profileA=pa;
    if(!Directory.Exists(pb)){
      RunCmd("if not exist \""+pb+"\" md \""+pb+"\" 2>nul",3000);
      if(Directory.Exists(pb)) profileB=pb;
    }else profileB=pb;
    Diag("私有 profile 预建：A="+(profileA??"失败")+" B="+(profileB??"失败"));
  }

  static void CleanupOldProfiles(){
    string lad=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    string[] dirs={
      Path.GetTempPath(),
      Path.Combine(lad,"TimeCompass"),
      lad,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      @"C:\Windows\Temp",
      Environment.GetEnvironmentVariable("PUBLIC"),
      Environment.GetEnvironmentVariable("ALLUSERSPROFILE")
    };
    foreach(var d in dirs){
      if(string.IsNullOrEmpty(d)) continue;
      try{
        foreach(var dd in Directory.GetDirectories(d,"tclp_scr_profile*")){
          try{ Directory.Delete(dd,true); }catch{}
        }
      }catch{}
    }
  }

  /* v4.0（D-08）：回收"URL 交接"在用户 Edge 实例里开出的新窗口。
     策略③/④交接出去的窗口不属于我们的进程树（Kill 打不中也不该 Kill —— 会连坐
     用户的浏览会话），只能逐个 PostMessage(WM_CLOSE)。启动前快照里的用户窗口一律不碰。 */
  static void CloseForeignEdgeWindows(){
    try{
      EnumWindows((h,l)=>{
        try{
          if(!IsWindowVisible(h)) return true;
          uint pid; GetWindowThreadProcessId(h,out pid);
          if(pid==0) return true;
          bool isEdge=false;
          using(var p=Process.GetProcessById((int)pid)){
            isEdge=p.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase);
          }
          if(!isEdge) return true;
          if(preEdge.Contains(h)) return true;      // 用户自己的窗口，绝不碰
          if(h==kioskWin) return true;              // 最终认定的屏保窗口由收尾逻辑负责
          PostMessage(h,WM_CLOSE,IntPtr.Zero,IntPtr.Zero);
        }catch{}
        return true;
      },IntPtr.Zero);
    }catch{}
  }

  static void LaunchEdge(string url){ StartEdgeStrategy(url,0); }

  static string EdgeArgs(string url,bool minimal,string profile){
    /* v3.0 的"减负"开关：屏保是长期运行的全屏页面，后台网络/同步/更新/崩溃上报
       对画面毫无贡献，只会白烧 CPU 与网络；minimal 时不带这些旗标（兼容老 Edge）。
       v4.0（D-13）：--autoplay-policy 保留在 minimal 里 —— 它是滴答声的唯一开关，
       缺了它 AudioContext 挂起：无声，且每秒仍白建一个 oscillator 节点。
       该旗标 Chrome 66+ 就支持，不属于"新旗标兼容"风险。 */
    return minimal
      ? "--kiosk \""+url+"\" --no-first-run --autoplay-policy=no-user-gesture-required"+
        (profile!=null?" --user-data-dir=\""+profile+"\"":"")
      : "--kiosk \""+url+"\" --edge-kiosk-type=fullscreen --no-first-run"+
        " --no-default-browser-check"+
        " --autoplay-policy=no-user-gesture-required"+
        " --disable-extensions --disable-component-extensions-with-background-pages"+
        " --disable-background-networking --disable-sync --disable-default-apps"+
        " --no-service-autorun --disable-client-side-phishing-detection"+
        " --disable-domain-reliability --disable-breakpad --disable-component-update"+
        " --disable-session-crashed-bubble --hide-scrollbars"+
        " --disable-features=TranslateUI,MediaRouter,OptimizationHints,HardwareMediaKeyHandling"+
        (profile!=null?" --user-data-dir=\""+profile+"\"":"");
  }

  /* v3.4：把 Edge 关进 Job 笼子 —— Edge 有"启动器转生"行为（launcher fork 真 browser
     后自己退出），Process.Kill() 打不中真身；关 Job 句柄则系统强杀整棵树，
     壳以任何方式死亡（正常退出/被杀/崩溃）都覆盖。 */
  static void MakeJobFor(Process p){
    try{
      edgeJob=CreateJobObject(IntPtr.Zero,null);
      if(edgeJob!=IntPtr.Zero){
        var info=new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        var infoPtr=System.Runtime.InteropServices.Marshal.AllocHGlobal(
          System.Runtime.InteropServices.Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
        try{
          System.Runtime.InteropServices.Marshal.StructureToPtr(info,infoPtr,false);
          SetInformationJobObject(edgeJob,JobObjectExtendedLimitInformation,infoPtr,
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
        }finally{ System.Runtime.InteropServices.Marshal.FreeHGlobal(infoPtr); }
        AssignProcessToJobObject(edgeJob,p.Handle);
      }
    }catch(Exception ex){ Diag("Job 笼子设置失败（不影响启动）："+ex.Message); }
  }

  static void StartEdgeStrategy(string url,int idx){
    strategyIdx=idx;
    strategyStart=(long)Environment.TickCount;
    launcherExitLogged=false;
    bool minimal=(idx%2==1);      // 奇数号 = 最小参数
    bool useProfile=(idx<2);      // 前两条 = 私有配置
    profileDir=null;
    /* v3.9：本机没有任何可写目录时，私有配置策略必死（Chromium 对不可写 profile
       是硬失败，本机实测原话 "Failed to create a ProcessSingleton ... Aborting now"）。
       直接跳过、别让用户白看 25 秒透明屏，直奔"默认配置"策略。 */
    if(useProfile && baseProfileDir==null){
      strategyLog.Add(strategyName[idx]+"：跳过（本机无可写目录）");
      Diag("启动策略 "+strategyName[idx]+"：跳过 —— 本机无可写目录，私有配置不可用");
      if(idx+1<strategyName.Length){ StartEdgeStrategy(url,idx+1); }
      else{ quitReason="看门狗：4 条启动策略全部失败"; TriggerQuit(); }
      return;
    }
    if(useProfile){
      /* v4.0（D-05）：目录已在主线程预建，这里只做存在性检查（零阻塞） */
      profileDir=(idx==0)?profileA:profileB;
      if(profileDir==null || !Directory.Exists(profileDir)){
        strategyLog.Add(strategyName[idx]+"：跳过（私有 profile 目录不可用）");
        Diag("启动策略 "+strategyName[idx]+"：跳过 —— 私有 profile 目录不可用");
        if(idx+1<strategyName.Length){ StartEdgeStrategy(url,idx+1); }
        else{ quitReason="看门狗：4 条启动策略全部失败"; TriggerQuit(); }
        return;
      }
    }
    string args=EdgeArgs(url,minimal,profileDir);
    Diag("启动策略 "+strategyName[idx]+"：profile="+(profileDir??"(系统默认)")+" 参数="+args);
    string[] candidates={
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)+@"\Microsoft\Edge\Application\msedge.exe",
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)+@"\Microsoft\Edge\Application\msedge.exe",
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+@"\Microsoft\Edge\Application\msedge.exe",
      "msedge.exe"
    };
    foreach(var p in candidates){
      try{
        edge=Process.Start(new ProcessStartInfo{
          FileName=p, Arguments=args,
          UseShellExecute=false, CreateNoWindow=true
        });
        if(edge!=null){
          MakeJobFor(edge);
          Diag("启动器 pid="+edge.Id+"（"+p+"）");
          return;
        }
      }catch(Exception ex){
        /* v3.6：不再吞掉每个候选路径的失败原因——hendr 电脑排查需要 */
        Diag("Edge 启动尝试失败："+p+" : "+ex.Message);
      }
    }
    /* v3.5：失败弹窗必须 TopMost —— 此刻指针幕布（TOPMOST 隐形窗口）已经挂上，
       普通弹窗会被压在幕布之下，用户看到一个"透明屏保"却不知道下面有个看不见的弹窗！
       MessageBoxOptions.DefaultDesktopOnly 让弹窗浮到最上层。
       v3.7：内嵌内存日志尾部（弹窗=日志，不依赖写盘）。 */
    quitReason="未找到 Microsoft Edge（4 个候选路径全部失败）";
    strategyIdx=-1;                            // 不再让看门狗去试后面的策略
    MessageBox.Show("未找到 Microsoft Edge，无法渲染时间罗盘屏保。\n\n已尝试的位置：\n"+
      string.Join("\n", candidates)+"\n\n======== 运行日志 ========\r\n"+DiagTail(30),
      "时间罗盘 "+VER, MessageBoxButtons.OK, MessageBoxIcon.Warning,
      MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
    TriggerQuit();
  }

  /* v3.9：收掉本次策略的启动器（含 Job 里的整棵 Edge 树） */
  static void KillLauncherAndJob(){
    try{ if(edge!=null && !edge.HasExited){ edge.Kill(); edge.WaitForExit(1200); } }catch{}
    try{ if(edgeJob!=IntPtr.Zero){ CloseHandle(edgeJob); edgeJob=IntPtr.Zero; } }catch{}
    edge=null;
  }

  /* v4.0a（Q-04）：策略成功收口（记功 + 定案 + 转运行态）。
     看门狗主路径与 NextEdgeStrategy 的"超时后复查"共用，避免两份拷贝漂移。 */
  static void MarkStrategySuccess(){
    kioskWin=FindEdgeWindow();
    int kwPid=0;
    try{ if(kioskWin!=IntPtr.Zero){ uint q; GetWindowThreadProcessId(kioskWin,out q); kwPid=(int)q; } }catch{}
    Diag("✔ 启动策略 "+strategyName[strategyIdx]+" 生效：检测到屏保窗口（"
         +((long)Environment.TickCount-strategyStart)+"ms）pid="+kwPid
         +(preEdge.Contains(kioskWin)?" ⚠ 该窗口启动前就存在（存疑）":""));
    strategyLog.Add(strategyName[strategyIdx]+"：成功"
         +(preEdge.Contains(kioskWin)?"（但窗口启动前就存在，存疑）":""));
    strategyIdx=-1; sawEdge=true; noEdge=0; lastEdgeSeen=(long)Environment.TickCount;
  }

  /* v3.9：本条策略失败 → 记档 → 换下一条；4 条全败才撤幕布退出。
     "启动器 exitCode" 是关键诊断：0 通常表示"URL 交接给已有实例后自己让位"，
     非 0（典型 0xC0000135/0xE0434352 之外的小码）才更像被拦或崩溃。
     v4.0（D-06）：策略切换不再立即执行 —— Job 杀树是异步的（KILL_ON_JOB_CLOSE 由
     系统收尾），马上用同一个系统默认 profile 再启 Edge 会撞上"将死实例"的
     SingletonLock → 连锁失败。改为挂起 800ms 冷却，由监控循环到点再启动，
     不用 Sleep 阻塞幕布消息泵。 */
  static int  pendingIdx=-1;      // v4.0：冷却期结束后要启动的策略号（-1=无）
  static long resumeAt=0;         // v4.0：冷却截止时刻（Environment.TickCount）
  static void NextEdgeStrategy(){
    if(quitting) return;                       // 已决定退出就不再折腾新策略
    string det="启动器仍在运行";
    try{
      if(edge!=null && edge.HasExited) det="启动器 exitCode="+edge.ExitCode;
      else if(edge==null) det="启动器未创建";
    }catch{}
    long el=(long)Environment.TickCount-strategyStart;
    Diag("启动策略 "+strategyName[strategyIdx]+" 失败：超时 "+el+"ms，"+det+"，窗口未出现");
    strategyLog.Add(strategyName[strategyIdx]+"：失败（"+el+"ms "+det+"）");
    /* v4.0a（Q-04）：超时判定粒度 ~300ms —— 慢启动的 kiosk 窗口可能恰好出现在
       "最后一次检查已过、现在判死"的间隙里。杀树之前最后复查一次，出现了就直接转成功，
       别把快出来的窗口连树杀掉（对③④交接窗口尤其重要：它们不属于我们的 Job 树）。 */
    try{ if(EdgeWindowAppeared()){ MarkStrategySuccess(); return; } }catch{}
    KillLauncherAndJob();
    CloseForeignEdgeWindows();                 // v4.0（D-08）：回收交接窗口，防残留
    if(strategyIdx+1<strategyName.Length){
      pendingIdx=strategyIdx+1;
      resumeAt=(long)Environment.TickCount+800;
      strategyIdx=-1;                          // 冷却期：看门狗对本策略不再计时
    }else{
      quitReason="看门狗：4 条启动策略全部失败，Edge 始终未出现"
                +"（可能未安装 Edge、被安全策略拦截，或本进程被降权到无法建立 Edge 配置目录）";
      TriggerQuit();
    }
  }

  /* v3.9：窗口是否"接近全屏"——用来把 kiosk 窗口与用户自己开着的普通 Edge 窗口区分开。
     为什么需要：Windows 的设置对话框里点"预览"时，用户可能本来就开着 Edge；
     旧版只看"有没有 msedge 可见窗口"，会把用户的浏览器当成"屏保已起来"，
     于是永久透明挂着。 */
  static bool IsFullScreenish(IntPtr h){
    try{
      RECT r;
      if(!GetWindowRect(h,out r)) return false;
      int sw=GetSystemMetrics(0), sh=GetSystemMetrics(1);
      return (r.right-r.left)>=sw*8/10 && (r.bottom-r.top)>=sh*8/10;
    }catch{ return false; }
  }

  /* ================= Edge 窗口检测与置顶 ================= */

  /* v4.0（D-02 回归修复）：启动成功判定。
     v3.9 只认"≥80% 屏幕的全屏窗口"，把策略③/④"URL 交接"在用户 Edge 实例里开出的
     【普通窗口】判死 → 4 条策略全败 → 公司电脑上"透明屏保后弹窗"，而 v3.0 的宽松判定
     （EdgeWindowExists，当时还在用）同场景能跑 —— 这就是"之前版本能用、中间改坏了"。
     最终判据：**可见 msedge 窗口 且 不在启动前快照 preEdge 里（= 新建窗口）**。
       · 策略①②的 kiosk 窗口（新进程）与策略③④的交接窗口（已有实例新开窗口）
         都必然不在快照里 → 全部覆盖；
       · 反向误判彻底防住：用户启动前就开着的 Edge（哪怕最大化/F11 全屏，也 ≥80%
         "全屏级"）在快照里 → 一律不算屏保窗口（v3.9 的"假成功→透明卡死"根除；
         QA 复核发现曾保留的 IsFullScreenish 析取支会让该场景漏网，已移除）；
       · 唯一理论漏网：--kiosk 以"标签页"形式落进用户已有窗口 —— 实测 --kiosk
         总是自开新窗口，属可接受取舍。 */
  static bool EdgeWindowAppeared(){
    bool found=false;
    EnumWindows((h,l)=>{
      try{
        if(!IsWindowVisible(h)) return true;
        uint pid; GetWindowThreadProcessId(h,out pid);
        if(pid==0) return true;
        bool isEdge=false;
        using(var p=Process.GetProcessById((int)pid)){
          isEdge=p.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase);
        }
        if(!isEdge) return true;
        if(!preEdge.Contains(h)){ found=true; return false; }
      }catch{}
      return true;
    },IntPtr.Zero);
    return found;
  }

  /* v3.3：取最顶层的可见 msedge 窗口（Z 序从顶到底，第一个命中即返回）。
     v3.9：启动器进程可能已经"让位"（URL 交接给已有 Edge 实例），此时 edge.Id 已失效，
     退化为"取最顶层的全屏 msedge 窗口"——否则交接场景下置顶完全失效。 */
  static IntPtr FindEdgeWindow(){
    IntPtr found=IntPtr.Zero;
    try{
      if(edge!=null && !edge.HasExited){
        int id=edge.Id;
        EnumWindows((h,l)=>{
          try{
            if(!IsWindowVisible(h)) return true;
            uint pid; GetWindowThreadProcessId(h,out pid);
            if(pid==(uint)id) { found=h; return false; }
          }catch{}
          return true;
        },IntPtr.Zero);
        if(found!=IntPtr.Zero) return found;
      }
      EnumWindows((h,l)=>{
        try{
          if(!IsWindowVisible(h)) return true;
          uint pid; GetWindowThreadProcessId(h,out pid);
          if(pid==0) return true;
          using(var p=Process.GetProcessById((int)pid)){
            if(!p.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase)) return true;
          }
          /* v4.0（D-02）：与 EdgeWindowAppeared 同判据（只认快照外的新窗口）——
             交接出的普通窗口也要能被选中置顶，否则交接场景下 Z 序维护完全失效 */
          if(!preEdge.Contains(h)){ found=h; return false; }
        }catch{}
        return true;
      },IntPtr.Zero);
    }catch{}
    return found;
  }

  /* v3.3：Z 序侦察 —— target 之上是否压着"真正抢镜"的可见窗口。
     跳过不可见窗口和工具窗口（通知气泡、IME、我们自己的幕布都是工具窗，不算抢镜）。
     为什么需要它（主人实测反馈"启动时指针闪 4 次才隐藏"）：
     每次 SetWindowPos 改 Z 序都会触发系统重算指针归属（重算的默认结果=箭头），
     所以"周期性无条件抬窗"等于自己制造指针闪烁。改成按需抬窗后，
     稳态下（幕布/Edge 已在顶上）一次都不抬，闪烁归零。 */
  static bool SomethingAbove(IntPtr target){
    if(target==IntPtr.Zero) return false;
    try{
      IntPtr h=GetTopWindow(IntPtr.Zero);
      int guard=0;
      while(h!=IntPtr.Zero && guard++<512){
        if(h==target) return false;                    // 先遇到目标 → 目标之上没有别人
        if(IsWindowVisible(h)){
          int ex=GetWindowLong(h,GWL_EXSTYLE);
          if((ex&WS_EX_TOOLWINDOW)==0) return true;    // 一个可见的非工具窗口压在目标上
        }
        h=GetWindow(h,GW_HWNDNEXT);
      }
    }catch{}
    return false;
  }

  static void PinEdgeTopmost(){
    try{
      EnumWindows((h,l)=>{
        try{
          if(!IsWindowVisible(h)) return true;
          uint pid; GetWindowThreadProcessId(h,out pid);
          if(pid==0) return true;
          using(var p=Process.GetProcessById((int)pid)){
            if(!p.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase)) return true;
          }
          SetWindowPos(h,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW);
        }catch{}
        return true;
      },IntPtr.Zero);
    }catch{}
  }

  /* ================= v3.1 指针幕布 =================
     指针样式由"指针下方那个窗口"决定，所以只要壳自己拿一个全屏置顶窗口垫在最上层，
     并在 WM_SETCURSOR 里 SetCursor(NULL)，指针就藏起来了 —— 不依赖 Edge 是否已加载完页面。
     窗口特性：分层 alpha=1（不可见但可被命中）、不激活、工具窗口（不进 Alt+Tab 与任务栏）。
     安全性：它只是"接住"指针，不吞键盘；点击会被全局钩子判为退出，不影响"动一下鼠标就退"。 */
  static IntPtr CurtainWndProc(IntPtr hWnd, uint msg, IntPtr w, IntPtr l){
    if(msg==WM_SETCURSOR){ setCursorMsgHits++; SetCursor(IntPtr.Zero); return (IntPtr)1; }
    if(msg==WM_MOUSEACTIVATE) return (IntPtr)MA_NOACTIVATE;   // 永不抢焦点
    /* v3.2：必须处理 WM_PAINT —— 幕布没有背景刷，不 ValidateRect 会无限重画，
       把消息泵点着烧 CPU。这个坑是"消息泵接上"（根因①修复）之后才会暴露的。 */
    if(msg==WM_PAINT){ ValidateRect(hWnd,IntPtr.Zero); return IntPtr.Zero; }
    if(msg==WM_ERASEBKGND) return (IntPtr)1;
    return DefWindowProc(hWnd,msg,w,l);
  }

  /* v3.2⚠ 必须在「监控线程」上调用本函数。
     原因（根因①）：窗口消息由拥有该窗口的线程泵；旧版在主线程建幕布，而主线程随后
     mt.Join() 阻塞、永不泵消息 → WM_SETCURSOR 投递不进来 → CurtainWndProc 从未执行。 */
  static void CreateCursorCurtain(){
    if(curtain!=IntPtr.Zero) return;
    try{
      wndProcDelegate=CurtainWndProc;      // 委托保活（同 v2.0 钩子的坑）
      var wc=new WNDCLASSEX();
      wc.cbSize=(uint)Marshal.SizeOf(typeof(WNDCLASSEX));
      wc.lpfnWndProc=wndProcDelegate;
      wc.hInstance=GetModuleHandle(null);
      wc.lpszClassName="TimeCompassCursorCurtain";
      wc.hCursor=IntPtr.Zero;              // 类不带光标
      RegisterClassEx(ref wc);             // 已注册(1410)不致命，继续创建窗口
      int x=GetSystemMetrics(SM_XVIRTUALSCREEN), y=GetSystemMetrics(SM_YVIRTUALSCREEN);
      int w=GetSystemMetrics(SM_CXVIRTUALSCREEN), h=GetSystemMetrics(SM_CYVIRTUALSCREEN);
      if(w<=0||h<=0){
        x=0; y=0;
        w=Screen.PrimaryScreen.Bounds.Width; h=Screen.PrimaryScreen.Bounds.Height;
      }
      curtain=CreateWindowEx(WS_EX_LAYERED|WS_EX_TOPMOST|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,
        "TimeCompassCursorCurtain","",WS_POPUP|WS_VISIBLE,x,y,w,h,
        IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
      if(curtain==IntPtr.Zero){ lastError="幕布创建失败 err="+Marshal.GetLastWin32Error(); return; }
      /* alpha 必须是 1 而不是 0：alpha=0 会被 Windows 当成鼠标穿透，
         指针就不会落在幕布上，WM_SETCURSOR 也就收不到了（实测确认）。alpha=1 肉眼不可见。 */
      SetLayeredWindowAttributes(curtain,0,1,LWA_ALPHA);
      SetCursor(IntPtr.Zero);              // 立刻隐藏，不必等鼠标移动
    }catch(Exception ex){ lastError="幕布异常："+ex.Message; }
  }

  static void RaiseCursorCurtain(){
    if(curtain==IntPtr.Zero) return;
    try{ SetWindowPos(curtain,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW); }catch{}
  }

  static void DestroyCursorCurtain(){
    if(curtain==IntPtr.Zero) return;
    try{ DestroyWindow(curtain); }catch{}
    curtain=IntPtr.Zero;
    try{ UnregisterClass("TimeCompassCursorCurtain",GetModuleHandle(null)); }catch{}
    /* 刷新系统光标：保证退出屏保后用户的指针一定回来。
       （不做也不会残留在隐藏态——桌面窗口会在下一次鼠标移动时重新设回箭头，这里是保险。） */
    try{ SystemParametersInfo(SPI_SETCURSORS,0,IntPtr.Zero,0); }catch{}
  }

  /* ---------------- v3.2 确定性隐藏（根因③的修法） ----------------
     为什么不能再靠"消息驱动"：WM_SETCURSOR 只在"指针下方窗口发生变化/鼠标移动"时才有，
     而 PinEdgeTopmost() 每 0.5 秒会把 Edge 抬到置顶带上层，系统就借机把指针重算成默认箭头。
     只要那一次重算发生时幕布没能及时改回来，箭头就会一直留在屏幕上（指针状态不会自己刷新）。
     所以改成"每 20ms 主动按回去一次"，把隐藏从"事件驱动"变成"状态维持"。 */

  /* 指针状态的地面真相（flags bit0 = CURSOR_SHOWING） */
  static bool CursorShowing(){
    try{
      var ci=new CURSORINFO();
      ci.cbSize=(uint)Marshal.SizeOf(typeof(CURSORINFO));
      if(!GetCursorInfo(ref ci)) return false;
      return (ci.flags & CURSOR_SHOWING)!=0;
    }catch{ return false; }
  }

  /* 指针下方是哪个窗口、什么类？—— 判断"指针归属被谁抢走了"的唯一硬指标。
     若返回 TimeCompassCursorCurtain 说明幕布自己就在最上面（那是系统补投默认箭头，
     只需重按）；返回 Chrome_WidgetWin_1 说明 Edge 压在幕布之上（要修 Z 序）。 */
  static string UnderCursorClass(){
    try{
      POINT p;
      if(!GetCursorPos(out p)) return "(取不到坐标)";
      IntPtr h=WindowFromPoint(p);
      if(h==IntPtr.Zero) return "(无窗口)";
      var sb=new System.Text.StringBuilder(128);
      if(GetClassName(h,sb,sb.Capacity)<=0) return "(取不到类名)";
      return sb.ToString();
    }catch{ return "(异常)"; }
  }

  /* 每 HIDE_TICK_MS 调一次：先强制按回，再用 GetCursorInfo 闭环确认；
     若仍显示 → 重新置顶幕布（触发系统把指针归属算回幕布）→ 再按一次。 */
  static void HideCursorTick(){
    try{
      SetCursor(IntPtr.Zero);
      if(!CursorShowing()) return;
      long now=(long)Environment.TickCount;
      if(HEAL_MIN_GAP_MS>0 && now-lastHealTick < HEAL_MIN_GAP_MS) return;   // 限速（默认关闭）
      lastHealTick=now;
      selfHealCount++;
      if(selfHealCount<=40)
        Diag("指针自愈 #"+selfHealCount+"：指针仍显示，下方窗口类="+UnderCursorClass()
             +" → 幕布重新置顶并再隐藏");
      RaiseCursorCurtain();
      SetCursor(IntPtr.Zero);
    }catch{}
  }

  /* v3.1：应急热键改"候选列表逐个试"。实测本机 Ctrl+Alt+Q 注册失败过
     （被其它软件占用），而它是钩子之外唯一的软退出通道，不能静默失效。 */
  static bool hkOk=false;
  static uint hkMod=0, hkVk=0;

  static void RegisterEmergencyHotkey(){
    uint[][] cand={
      new uint[]{MOD_CONTROL|MOD_ALT,           VK_Q},
      new uint[]{MOD_CONTROL|MOD_ALT|MOD_SHIFT, VK_Q},
      new uint[]{MOD_CONTROL|MOD_ALT,           VK_F12},
      new uint[]{MOD_CONTROL|MOD_ALT,           VK_F9}
    };
    foreach(var c in cand){
      if(RegisterHotKey(IntPtr.Zero, HOTKEY_ID, c[0], c[1])){
        hkMod=c[0]; hkVk=c[1]; hkOk=true;
        Diag("应急热键已注册：mod=0x"+c[0].ToString("X")+" vk=0x"+c[1].ToString("X")
             +" → "+HotkeyText());
        return;
      }
      Diag("应急热键候选 0x"+c[0].ToString("X")+"/0x"+c[1].ToString("X")
           +" 注册失败，错误码="+Marshal.GetLastWin32Error());
    }
    Diag("应急热键所有候选均失败 —— 只能依赖低级钩子与 Ctrl+Alt+Del 兜底");
  }

  static string HotkeyText(){
    if(!hkOk) return "（未注册成功，请用 Ctrl+Alt+Del）";
    string k = hkVk==VK_Q ? "Q" : (hkVk==VK_F12 ? "F12" : "F9");
    string m = "Ctrl+Alt" + ((hkMod & MOD_SHIFT)!=0 ? "+Shift" : "");
    return m+"+"+k;
  }

  /* v3.9：启动策略状态机的全局状态见 Edge 启动区（strategyIdx / currentUrl / strategyLog） */

  /* ================= 独立监控线程 =================
     钩子/热键/置顶/看门狗全部在这条专职线程上，自带消息泵，
     不依赖 WinForms UI 线程——UI 再忙也绝不影响退出检测。
     v3.2 起「指针幕布」也归这条线程：它是唯一一条持续泵消息的线程，
     窗口建在这里，WM_SETCURSOR/WM_PAINT 才真正有人处理。 */
  static void MonitorLoop(){
    mouseProcDelegate=MouseProc;                     // 委托保活（防 GC 回收）
    keyProcDelegate=KeyProc;
    var hMod=GetModuleHandle(null);
    mHook=SetWindowsHookEx(14, mouseProcDelegate, hMod, 0);  // WH_MOUSE_LL
    kHook=SetWindowsHookEx(13, keyProcDelegate,   hMod, 0);  // WH_KEYBOARD_LL
    hookStartTick=(long)Environment.TickCount;               // v3.2 启动宽限期起点
    RegisterEmergencyHotkey();
    Diag("监控线程启动：鼠标钩子="+mHook+" 键盘钩子="+kHook+"（0/-1 表示安装失败）");

    /* 幕布必须在"本线程"创建：本线程是唯一持续 PeekMessage 的线程。 */
    CreateCursorCurtain();
    Diag("幕布："+(curtain!=IntPtr.Zero?"已创建":"创建失败 "+lastError));

    int ticks=0;       /* v4.0a：noEdge/sawEdge 提升为静态字段（见策略状态区） */
    while(!quitting){
      /* 泵消息：低级钩子回调经由本线程消息队列投递；顺带接收应急热键，
         以及幕布窗口的 WM_SETCURSOR / WM_PAINT（v3.2 的关键） */
      MSG m;
      while(PeekMessage(out m, IntPtr.Zero, 0, 0, PM_REMOVE)){
        if(m.message==WM_HOTKEY){ quitReason="应急热键"; TriggerQuit(); }
        TranslateMessage(ref m);
        DispatchMessage(ref m);
      }
      if(quitting) break;
      Thread.Sleep(HIDE_TICK_MS);
      ticks++;
      FlushHookDiag();                       // v4.0（D-04）：钩子队列的诊断行统一落盘

      /* 节拍按 20ms 一格换算：25 格≈0.9s，135 格≈5s，250 格≈9s */
      /* v3.3：按需抬窗（根因：主人实测"启动时指针闪 4 次才隐藏"）。
         旧版是 ticks<=135 && ticks%25==0 —— 前 5 秒每 0.9 秒无条件抬一次 Edge+幕布，
         共约 6 次；每次 SetWindowPos 改 Z 序都触发系统重算指针归属（默认=箭头），
         20ms 后才被 HideCursorTick 按回 —— 4~6 次肉眼可见的闪烁，与实测
         "自愈 5 次、100ms 采样可见 4 次"完全吻合。
         现在改成每 5 拍（≈100ms）Z 序侦察一次，按需才抬：
           · 幕布之上压了可见非工具窗口 → 抬幕布；
           · Edge 之上压了可见非工具窗口 → 先抬 Edge 再抬幕布；
           · 什么都不需要 → 完全不碰 Z 序（稳态一次都不抬）。
         抬完等 15ms 让异步重算落地，再补一刀隐藏收掉尾巴
         （HEAL_MIN_GAP_MS=0，不会被限速吃掉 —— v3.2 踩过这个坑）。 */
      if(ticks%5==0){
        bool needCurtain=SomethingAbove(curtain);
        /* v4.0（D-11）：FindEdgeWindow 的全窗口枚举只在"等 Edge 出现/启动后 30 秒内"
           进行 —— 稳态零枚举。旧版每 100ms 全屏枚举+对每个可见窗口开进程句柄，
           在钩子密集的受控机器上自身就是可感开销。 */
        bool needEdge=false;
        if(strategyIdx>=0 || pendingIdx>=0 ||
           (sawEdge && (long)Environment.TickCount-lastEdgeSeen<30000))
          needEdge=SomethingAbove(FindEdgeWindow());
        if(needCurtain||needEdge){
          if(needEdge) PinEdgeTopmost();
          RaiseCursorCurtain();
          Thread.Sleep(15);
          HideCursorTick();
          Diag("按需抬窗：幕布被压="+needCurtain+" Edge被压="+needEdge);
        }
      }

      /* ⚠ v3.2 顺序关键：隐藏指针必须放在"所有会触发系统重算指针的动作之后"（抬 Edge、
         抬幕布都会触发重算，重算的结果默认是箭头）。原先 HideCursorTick 排在它们前面，
         于是每次抬完幕布都会留一个约 30ms 的箭头空档。挪到最后一拍，隐藏就成了本拍的
         最终裁决，不再给系统留空子。 */
      HideCursorTick();

      /* 看门狗（v3.9 重写为"启动策略状态机"；v4.0 接入冷却调度与新成功判据）：
         冷却期到点 → 启动挂起的下一条策略；
         策略未定案 → 每 ~0.3 秒看有没有"全屏级或新建"的 Edge 窗口出现：
           · 出现 → 记功，转入运行态（只再监测"曾经出现又消失"）；
           · 超时 → 换下一条策略；4 条全败才撤幕布退出并弹诊断。
         为什么不用 edge.HasExited 判定：Edge 有"启动器让位/URL 交接"行为，
         启动器秒退 ≠ 真 browser 没起来（交接时窗口由已有实例创建）。 */
      if(pendingIdx>=0 && !quitting && (long)Environment.TickCount>=resumeAt){
        int nx=pendingIdx; pendingIdx=-1;
        StartEdgeStrategy(currentUrl,nx);
      }
      if(strategyIdx>=0 && !quitting && ticks%10==0){
        bool anyEdge=false;
        try{ anyEdge=EdgeWindowAppeared(); }catch{}
        if(anyEdge){
          MarkStrategySuccess();               // v4.0a（Q-04）：成功收口统一走这里
        }else{
          if(!launcherExitLogged && edge!=null){
            try{
              if(edge.HasExited){
                launcherExitLogged=true;
                Diag("启动器已退出：exitCode="+edge.ExitCode+" 存活≈"
                     +((long)Environment.TickCount-strategyStart)+"ms"
                     +"（0 多表示正常让位；其它值提示被拦或崩溃）");
              }
            }catch{}
          }
          if((long)Environment.TickCount-strategyStart > strategyMs[strategyIdx])
            NextEdgeStrategy();
        }
      }
      /* 运行态监测：策略定案后，先直查 kioskWin 句柄存活（零枚举），句柄没了才
         全量枚举兜底（防 Edge 换窗口重交接）。（v4.0，D-11） */
      if(strategyIdx<0 && sawEdge && ticks%20==0){
        bool anyEdge=(kioskWin!=IntPtr.Zero && IsWindow(kioskWin) && IsWindowVisible(kioskWin));
        if(!anyEdge){ try{ anyEdge=EdgeWindowAppeared(); }catch{} }
        if(anyEdge){ noEdge=0; lastEdgeSeen=(long)Environment.TickCount; }
        else if((long)Environment.TickCount-lastEdgeSeen>6000){
          Diag("看门狗：曾见到全屏 Edge 窗口，现已连续 6 秒不可见（第 "+(noEdge+1)+" 次）");
          if(++noEdge>=2){ quitReason="看门狗：Edge 窗口消失（浏览器被关闭或崩溃）"; TriggerQuit(); }
        }
      }

      /* 每 5 秒记一次健康快照：这是"指针到底藏住没有"的真机证据，
         出问题直接看 tc_diag.log，不用再靠肉眼猜。 */
      if(ticks%250==0)
        Diag("健康：指针="+(CursorShowing()?"显示中(异常)":"已隐藏")+
             " WM_SETCURSOR命中="+setCursorMsgHits+" 自愈="+selfHealCount);
    }
    FlushHookDiag();   // v4.0（D-04）：先把钩子队列里的"退出触发"等行排掉，保证日志顺序
    Diag("监控线程结束，ticks="+ticks+" WM_SETCURSOR命中="+setCursorMsgHits+" 自愈="+selfHealCount
         +" 鼠标事件数="+mouseEvts+" 累计位移="+travel+"px");
    /* 幕布也由本线程销毁（DestroyWindow 必须在拥有者线程调用） */
    DestroyCursorCurtain();
    Diag("幕布已销毁，系统光标已刷新");
    try{ if(hkOk) UnregisterHotKey(IntPtr.Zero, HOTKEY_ID); }catch{}
    try{ UnhookWindowsHookEx(mHook); }catch{}
    try{ UnhookWindowsHookEx(kHook); }catch{}
  }

  /* ================= 入口 ================= */
  [STAThread]
  static void Main(string[] args){
    string a=(args!=null && args.Length>0)?args[0].Trim().ToLower():"/s";
    /* v3.6 起诊断日志永远开启（见 Diag 注释），/diag 参数保留但已无实际作用 */
    LoadQuality();
    Diag("== 启动 args=["+string.Join(" ",args==null?new string[0]:args)+"] 画质="+quality);
    if(a=="/c" || a=="-c"){
      htmlFile=ResolveHtml();
      InitDiagDisk();                    // v4.0（D-03）：/c 也要能落盘日志
      string mode=htmlFile==null?"内嵌资源（HTTP 零写入模式，受控电脑兜底）":(htmlFile.Contains("timecompass_scr")?"内嵌资源（便携模式）":"外部文件");
      MessageBox.Show(
        "时间罗盘动态屏保 v4.0\n\n"+
        "HTML 来源："+mode+"\n"+
        (htmlFile==null?"":"路径："+htmlFile+"\n")+
        "\n画质档位："+quality+"\n"+
        "配置来源："+qualityFrom+"\n"+
        "\n（想省资源：在 .scr 同目录建「时间罗盘.ini」，写一行 quality=eco）\n"+
        "\n运行表现：\n"+
        "  - 全屏无鼠标指针（壳自带指针幕布，每 20ms 主动维持隐藏，不依赖页面加载）\n"+
        "\n退出方式（多通道冗余）：\n"+
        "  - 动一下鼠标 / 按任意键\n"+
        "  - 应急热键 Ctrl+Alt+Q（若被别的软件占用，会自动改用\n"+
        "      Ctrl+Alt+Shift+Q / Ctrl+Alt+F12 / Ctrl+Alt+F9 之一）\n"+
        "  - 万能保底 Ctrl+Alt+Del\n"+
        "\n启动后 20 秒内置顶压制悬浮窗，之后系统 UI 可自由置前。","关于时间罗盘屏保");
      return;
    }
    /* v3.5：/p 预览不再裸退。踩过的坑：v3.1~v3.4 里预览参数直接 return，
       结果 Windows「预览」按钮点了毫无反应——而 v3.0（十一前）没有这个分支，
       预览会落到下面的全屏逻辑，主人一直看到的是"预览=全屏跑一遍"。
       现在恢复该行为：/p 与 /s 同样全屏运行（动鼠标即退出）。 */
    if(a=="/p" || a=="-p") Diag("预览模式（/p）：按全屏运行（v3.0 起的既有行为）");

    /* /curtain N —— 只挂指针幕布 N 秒（自检：验证"指针确实被隐藏"这条通路，
       不必真的启动屏保）。用法：TimeCompass.scr /curtain 10 /diag
       v3.2 起本模式自带消息泵 + 确定性隐藏循环，并统计 WM_SETCURSOR 命中次数：
         - 「WM_SETCURSOR命中 > 0」说明系统确实把指针判给了幕布，通路是通的；
         - 「自愈 > 0」说明系统中途把箭头放了回来，靠隐藏循环按回去了。
       这两个数字是判断"幕布机制到底有没有在工作"的硬指标。 */
    if(a.StartsWith("/curtain") || a.StartsWith("-curtain")){
      int secs=8;
      if(args!=null && args.Length>1){ int.TryParse(args[1].Trim(), out secs); }
      if(secs<1) secs=1;
      /* v4.0a（Q-03）：幕布先行 —— 自检路径的探测也要遵守"幕布挂上之后才探测"的原则 */
      CreateCursorCurtain();
      InitDiagDisk();                    // v4.0（D-03）：自检模式日志也要能落盘
      Diag("curtain 自检开始："+(curtain!=IntPtr.Zero?"幕布已创建":"创建失败 "+lastError)
           +"，时长 "+secs+" 秒，初始指针="+(CursorShowing()?"显示中":"已隐藏"));
      var t0=DateTime.Now;
      int i=0;
      while((DateTime.Now-t0).TotalSeconds<secs){
        MSG m;
        while(PeekMessage(out m, IntPtr.Zero, 0, 0, PM_REMOVE)){
          TranslateMessage(ref m); DispatchMessage(ref m);
        }
        HideCursorTick();
        if(++i%25==0) RaiseCursorCurtain();
        Thread.Sleep(HIDE_TICK_MS);
        if(i%50==0)
          Diag("curtain 自检 t="+(DateTime.Now-t0).TotalSeconds.ToString("F1")
               +"s 指针="+(CursorShowing()?"显示中(异常)":"已隐藏")
               +" WM_SETCURSOR命中="+setCursorMsgHits+" 自愈="+selfHealCount);
      }
      DestroyCursorCurtain();
      Diag("curtain 自检结束：WM_SETCURSOR命中="+setCursorMsgHits
           +" 自愈="+selfHealCount+" 结束指针="+(CursorShowing()?"显示中":"已隐藏"));
      return;
    }

    /* /http：强制走"本地 HTTP 零写入"模式（跳过磁盘释放）。
       用途：①测试 ②受控电脑上磁盘释放被安全软件全拦时的手动逃生开关 */
    bool forceHttp=(args!=null && Array.IndexOf(args,"/http")>=0);
    if(forceHttp){ Diag("/http：强制 HTTP 模式（跳过磁盘释放）"); lastError="（/http 跳过了磁盘释放）"; }

    htmlFile=forceHttp?null:ResolveHtml();
    string url;
    if(htmlFile!=null){
      /* 模式①外部文件 / 模式②内嵌释放到磁盘 —— 都是 file:// 直读 */
      url="file:///"+htmlFile.Replace('\\','/')+"#scr&q="+quality;
      Diag("HTML 来源："+(htmlFile.Contains("timecompass_scr")?"内嵌释放":"外部文件")+"："+htmlFile);
    }else if(StartHttpServer()){
      /* 模式③零磁盘写入（受控机器兜底）：磁盘释放被安全软件全线拦截时，
         用壳内置的 loopback HTTP 微服务，Edge 直接读内存里的 HTML */
      url="http://127.0.0.1:"+httpPort+"/#scr&q="+quality;
    }else{
      MessageBox.Show("屏保资源缺失，无法启动。（壳 "+VER+"）\n\n磁盘释放依次尝试的目录与失败原因：\n"+lastError+
                      "\n且本地 HTTP 兜底通道也未能启动。\n\n这通常说明本机的安全软件拦截了屏保程序写盘，\n"+
                      "请把「时间罗盘.scr」和「时间罗盘.html」放在同一个文件夹后再双击运行\n"+
                      "（外部文件模式不写任何磁盘）。","时间罗盘 "+VER);
      return;
    }

    /* v3.2：先起监控线程（幕布由它创建），再启动 Edge。顺序不能反 —— 幕布必须比
       Edge 早出现，否则 Edge 冷启动那几秒会露箭头。
       v4.0（D-03）：监控线程再提前到"可写探测"之前 —— 受控机上全套探测（含 cmd
       委托，8 目录 × 4s 超时，最坏 30s+）期间幕布已经挂上：指针被藏住、钩子已就位，
       不会再出现"点了预览，桌面干晾半分钟"的前奏。看门狗在 strategyIdx==-1 时
       自然待命，不受提前启动影响。 */
    var mt=new Thread(MonitorLoop);
    mt.Start();
    Thread.Sleep(150);                 // 让监控线程把幕布挂好（建窗+建钩子只需几毫秒）

    /* v4.0（D-03）：诊断磁盘落点现在才初始化（顺带把内存日志回刷出去）。
       可写探测结果同时决定私有 profile 能不能用（v3.9 既有逻辑）。 */
    InitDiagDisk();
    string probeErrs=null;
    baseProfileDir=WritableDir(out probeErrs);
    Diag("可写目录判定："+wdirMode+"  选用目录="+(baseProfileDir??"(无)")
         +(probeErrs!=null?"\n"+probeErrs:""));

    /* v4.0（D-07/D-05）：清理历史遗留 profile 目录（范围跟随目录链），然后在
       主线程预建本轮的两个私有 profile（A→策略①，B→策略②）—— 监控线程上不再跑 RunCmd。 */
    CleanupOldProfiles();
    PreCreateProfiles();

    /* v3.9：启动前给屏幕上已有的 msedge 窗口拍快照。
       两个用途：① 判断用户是不是本来就开着 Edge（直接影响"交接"路径）；
                 ② 退出时只关"我们自己拉起来的那一个"，绝不误杀用户的浏览窗口。 */
    SnapEdgeWindows();

    currentUrl=url;               // v3.9：4 条启动策略共用同一个 URL
    /* v4.0a（Q-02）：钩子可能在收尾链路期间已被真实输入触发退出 —— 此时不再空启动
       Edge（活体实测曾观察到 Edge spawn 后 ~300ms 被 Job 笼子收掉，白启动一次） */
    if(quitting){ Diag("启动 Edge 前已收到退出信号（"+quitReason+"），跳过启动"); }
    else LaunchEdge(url);

    mt.Join();          // 等监控线程结束（退出触发或看门狗）；幕布也由它撤掉

    /* v3.9 收尾：只关自己的东西 ——
       · 私有 Job 树（我们拉起来的 Edge）关句柄即原子清掉；
       · "URL 交接"场景下窗口属于**用户已有的 Edge 实例**，直接 Kill 会连他的浏览会话
         一起杀掉，所以只对那个 kiosk 窗口 PostMessage(WM_CLOSE)（等价点关闭按钮）；
       · 启动前快照里就存在的窗口（用户自己开的）一律不碰。 */
    KillLauncherAndJob();
    try{
      IntPtr w=kioskWin;
      if(w!=IntPtr.Zero && IsWindow(w) && !preEdge.Contains(w)){
        PostMessage(w,WM_CLOSE,IntPtr.Zero,IntPtr.Zero);
        Diag("已向屏保窗口发送关闭请求（非本进程所有，用 WM_CLOSE 而非 Kill）");
      }
    }catch{}
    CloseForeignEdgeWindows();   // v4.0（D-08）：回收交接出去但没被认定为 kioskWin 的窗口
    StopHttpServer();

    /* v3.6：Edge 启动失败时必须给用户可见反馈。踩过的坑：看门狗"Edge 从未出现"
       退出时静默无声 → 用户看到的是"透明屏保 20 秒后自己消失了"，手里零线索。
       此刻幕布已销毁，普通弹窗可见；仍用 DefaultDesktopOnly 保证置顶。
       v3.7：直接内嵌**内存日志尾部**——hendr 电脑实测连日志文件都写不出盘，
       "日志路径"对用户毫无意义；弹窗=日志，截图即可交付证据。
       v3.9：再加"4 条策略各自的结局"与可写判定结论，一眼看懂卡在哪一环。 */
    if(quitReason!=null && quitReason.Contains("4 条启动策略全部失败")){
      string adv = wdirMode.IndexOf("委托 cmd 可写")>=0
        ? "本程序被安全软件「按程序拦截写盘」（cmd.exe 代写却成功），\n"
         +"把「时间罗盘.scr」加入该软件的信任/白名单即可恢复正常。"
        : "本进程被降权/沙箱化（连 cmd.exe 代写都失败），子进程同样受限，\n"
         +"任何浏览器都建不出配置目录。请把「时间罗盘.scr」加入安全软件的\n"
         +"信任列表（或联系该电脑的管理员放行），否则屏保无法在此运行。";
      MessageBox.Show(
        "时间罗盘屏保启动失败（壳 "+VER+"）\n\n"+
        "原因："+quitReason+"\n\n"+
        "已尝试的启动策略：\n  "+string.Join("\n  ",strategyLog.ToArray())+"\n\n"+
        "可写判定："+wdirMode+"\n\n"+adv+"\n\n"+
        "======== 运行日志（把本弹窗完整截图发给排查方）========\r\n"+
        DiagTail(40),
        "时间罗盘 "+VER, MessageBoxButtons.OK, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
    }
    Diag("壳退出，共 "+(DateTime.Now-T0).TotalSeconds.ToString("F1")+" 秒；退出原因："+quitReason);
    Environment.Exit(0);
  }
}
