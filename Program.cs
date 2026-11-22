using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BingWallpapers;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BingWallpapers.Show"))
        using (var instance = new Mutex(true, @"Local\BingWallpapers.Instance", out bool created))
        {
            if (!created)
            {
                signal.Set();
                return;
            }

            // Windows 7 can default to older TLS protocols despite supporting TLS 1.2.
            if (Environment.OSVersion.Version <= new Version(6, 1, 7601))
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var form = new MainForm())
            {
                var handle = form.Handle;
                var registered = ThreadPool.RegisterWaitForSingleObject(signal, (_, expired) =>
                {
                    if (!form.IsDisposed && form.IsHandleCreated)
                    {
                        try { form.BeginInvoke(new Action(form.ShowFromTray)); }
                        catch (InvalidOperationException) when (form.IsDisposed || !form.IsHandleCreated) { }
                    }
                }, null, Timeout.Infinite, false);
                try
                {
                    Application.Run(form);
                }
                finally
                {
                    registered.Unregister(null);
                }
            }
        }
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

    public static void Apply(string jpegPath)
    {
        string path = jpegPath;
        if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor == 1)
        {
            Directory.CreateDirectory(Settings.Profile);
            path = Path.Combine(Settings.Profile, "desktop.bmp");
            using (var image = Image.FromFile(jpegPath))
                image.Save(path, ImageFormat.Bmp);
        }

        if (!SystemParametersInfo(SetDesktopWallpaper, 0, path, UpdateUserProfile | BroadcastChange))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 无法设置桌面壁纸。");
    }
}
