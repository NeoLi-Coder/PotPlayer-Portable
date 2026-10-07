using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class PotPlayerPortable : ApplicationContext
{
    private const int Right = 0x27;
    private readonly string player;
    private readonly HashSet<uint> players = new HashSet<uint>();
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private readonly Stopwatch pressed = new Stopwatch();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly HookCallback callback;
    private IntPtr hook, target;
    private bool down, accelerated, cancelled, seenPlayer;
    private long lastScan = -500;

    [STAThread]
    private static void Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string player = Path.Combine(root, @"x64\PotPlayerMini64.exe");
        try
        {
            if (!File.Exists(player)) throw new FileNotFoundException("缺少播放器：" + player);
            string name;
            using (SHA256 hash = SHA256.Create())
                name = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-", "");
            bool owner;
            using (Mutex mutex = new Mutex(true, @"Local\PotPlayerPortable-" + name, out owner))
            {
                // 重复打开视频只转交给播放器，当前目录保持一个按键监听器。
                PotPlayerPortable context = owner ? new PotPlayerPortable(player) : null;
                try
                {
                    StringBuilder command = new StringBuilder();
                    foreach (string arg in args) { if (command.Length > 0) command.Append(' '); command.Append(Quote(arg)); }
                    Process.Start(new ProcessStartInfo(player, command.ToString()) {
                        WorkingDirectory = Path.GetDirectoryName(player), UseShellExecute = false
                    });
                    if (owner) Application.Run(context);
                }
                finally
                {
                    if (context != null) context.Dispose();
                    if (owner) mutex.ReleaseMutex();
                }
            }
        }
        catch (Exception error) { MessageBox.Show(error.Message, "PotPlayer 便携版", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string Quote(string value)
    {
        StringBuilder result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }

    private PotPlayerPortable(string file)
    {
        player = file;
        callback = Keyboard;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用右方向键功能。");
        timer.Interval = 25;
        timer.Tick += Tick;
        timer.Start();
    }

    private void ScanPlayers()
    {
        players.Clear();
        foreach (Process process in Process.GetProcessesByName("PotPlayerMini64"))
        {
            using (process)
            {
                try
                {
                    if (String.Equals(process.MainModule.FileName, player, StringComparison.OrdinalIgnoreCase))
                        players.Add((uint)process.Id);
                }
                catch (Win32Exception) { /* 其他权限的播放器不接管。 */ }
                catch (InvalidOperationException) { /* 查询期间已退出的进程不接管。 */ }
            }
        }
        if (players.Count > 0) seenPlayer = true;
    }

    private static bool Modifier()
    {
        foreach (int key in new int[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
        return false;
    }

    private static string ClassName(IntPtr window)
    {
        StringBuilder name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private bool PlaybackWindow(IntPtr window)
    {
        uint pid;
        uint thread = GetWindowThreadProcessId(window, out pid);
        if (!players.Contains(pid) || !ClassName(window).StartsWith("PotPlayer", StringComparison.Ordinal)) return false;
        GuiThreadInfo info = new GuiThreadInfo();
        info.size = Marshal.SizeOf(info);
        if (!GetGUIThreadInfo(thread, ref info) || (info.flags & 0x1C) != 0) return false;
        string focus = ClassName(info.focus).ToLowerInvariant();
        return !focus.Contains("edit") && !focus.Contains("list") && !focus.Contains("tree") &&
            !focus.Contains("combo") && !focus.Contains("trackbar");
    }

    private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == Right)
        {
            bool release = message.ToInt64() == 0x101 || message.ToInt64() == 0x105;
            if (down)
            {
                if (release)
                {
                    if (accelerated) SetSpeed(target, false);
                    else if (!cancelled && target == GetForegroundWindow() && !Modifier() && PlaybackWindow(target))
                        PostMessage(target, 0x400, new IntPtr(0x5010), new IntPtr(Right));
                    down = accelerated = cancelled = false;
                    target = IntPtr.Zero;
                }
                return new IntPtr(1);
            }
            IntPtr foreground = GetForegroundWindow();
            if (!release && !Modifier() && PlaybackWindow(foreground))
            {
                target = foreground;
                down = true;
                pressed.Restart();
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private void Tick(object sender, EventArgs args)
    {
        if (lifetime.ElapsedMilliseconds - lastScan >= 500)
        {
            ScanPlayers();
            lastScan = lifetime.ElapsedMilliseconds;
            if (players.Count == 0 && (seenPlayer || lifetime.ElapsedMilliseconds >= 10000)) { ExitThread(); return; }
        }
        if (!down) return;
        if (target != GetForegroundWindow() || Modifier() || !PlaybackWindow(target))
        {
            if (accelerated) SetSpeed(target, false);
            accelerated = false;
            cancelled = true;
        }
        if (!cancelled && !accelerated && pressed.ElapsedMilliseconds >= 300)
        {
            SetSpeed(target, true);
            accelerated = true;
        }
    }

    private static void SetSpeed(IntPtr window, bool fast)
    {
        // “正常速度”会切换到上次速度，先降至最小速度，再恢复才能固定为 1 倍。
        for (int i = 0; i < 150; i++) PostMessage(window, 0x111, new IntPtr(10247), IntPtr.Zero);
        PostMessage(window, 0x111, new IntPtr(20010), IntPtr.Zero);
        if (fast)
            for (int i = 0; i < 20; i++) PostMessage(window, 0x111, new IntPtr(10248), IntPtr.Zero);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            timer.Dispose();
            if (accelerated) SetSpeed(target, false);
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
        base.Dispose(disposing);
    }

    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int size, flags;
        public IntPtr active, focus, capture, menuOwner, moveSize, caret;
        public int left, top, right, bottom;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr value, IntPtr data);
}
