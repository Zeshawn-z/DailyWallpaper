using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BingWallpapers;

internal static class Program
{
    public static string Version => typeof(Program).Assembly.GetName().Version.ToString(3);

    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        Run(args);
    }

    private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args) =>
        new AssemblyName(args.Name).Name == "AntdUI"
            ? Assembly.Load(ReadResource("BingWallpapers.Dependencies.AntdUI.dll"))
            : null;

    internal static byte[] ReadResource(string name)
    {
        using (var resource = typeof(Program).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded resource: " + name))
        using (var data = new MemoryStream())
        {
            resource.CopyTo(data);
            return data.ToArray();
        }
    }

    // Attach the resolver before the JIT compiles code that uses AntdUI.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool startInTray = Array.IndexOf(args, "--tray") >= 0;
        string signalName = @"Local\Dayframe.Show." + Version;
        using (var instance = new Mutex(false, @"Local\BingWallpapers.Instance"))
        {
            while (!TryEnter(instance))
            {
                if (startInTray)
                    return;
                if (EventWaitHandle.TryOpenExisting(signalName, out var running))
                {
                    using (running) running.Set();
                    return;
                }
                try { Strings.SetLanguage(Settings.Load().Language); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                    error is System.Runtime.Serialization.SerializationException)
                {
                    // An unreadable preference file must not stop the old-version notice.
                }
                if (MessageBox.Show(Strings.Get("An older version is still running in the tray. Exit it there, then choose Retry to open this version."),
                    Strings.AppTitle, MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry)
                    return;
            }

            try
            {
                // Windows 7 can default to older TLS protocols despite supporting TLS 1.2.
                if (Environment.OSVersion.Version <= new Version(6, 1, 7601))
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName))
                using (var form = new MainForm(startInTray))
                {
                    _ = form.Handle;
                    var registered = ThreadPool.RegisterWaitForSingleObject(signal, (_, expired) =>
                    {
                        if (!form.IsDisposed && form.IsHandleCreated)
                        {
                            try { form.BeginInvoke(new Action(form.ShowFromTray)); }
                            catch (InvalidOperationException) when (form.IsDisposed || !form.IsHandleCreated) { }
                        }
                    }, null, Timeout.Infinite, false);
                    try { Application.Run(form); }
                    finally { registered.Unregister(null); }
                }
            }
            finally { instance.ReleaseMutex(); }
        }
    }

    private static bool TryEnter(Mutex instance)
    {
        try { return instance.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }
}

internal static class DesktopWallpaper
{
    private const uint SetDesktopWallpaper = 0x0014;
    private const uint UpdateUserProfile = 0x01;
    private const uint BroadcastChange = 0x02;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string path, uint flags);

    public static void Apply(string jpegPath, string fit)
    {
        string path = jpegPath;
        if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1)
        {
            Directory.CreateDirectory(Settings.Profile);
            path = Path.Combine(Settings.Profile, "desktop.bmp");
            using (var image = Image.FromFile(jpegPath))
                image.Save(path, ImageFormat.Bmp);
        }

        using (var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
        {
            string style = fit == "Fit" ? "6" : fit == "Stretch" ? "2" : fit == "Center" || fit == "Tile" ? "0" : "10";
            var previousStyle = desktop.GetValue("WallpaperStyle");
            var previousTile = desktop.GetValue("TileWallpaper");
            try
            {
                desktop.SetValue("WallpaperStyle", style);
                desktop.SetValue("TileWallpaper", fit == "Tile" ? "1" : "0");
                if (!SystemParametersInfo(SetDesktopWallpaper, 0, path, UpdateUserProfile | BroadcastChange))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), Strings.Get("Windows could not apply this wallpaper."));
            }
            catch
            {
                Restore(desktop, "WallpaperStyle", previousStyle);
                Restore(desktop, "TileWallpaper", previousTile);
                throw;
            }
        }
    }

    private static void Restore(RegistryKey key, string name, object value)
    {
        if (value == null)
            key.DeleteValue(name, false);
        else
            key.SetValue(name, value);
    }
}

internal static class StartWithWindows
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Dayframe";

    public static bool Enabled()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(Run))
            return key?.GetValue(Name) is string;
    }

    public static void Set(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Run))
        {
            if (enabled)
                key.SetValue(Name, $"\"{Application.ExecutablePath}\" --tray");
            else
                key.DeleteValue(Name, false);
        }
    }
}
