using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quarkshot.Models;

namespace Quarkshot.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<WindowLayer> _windowLayers = [];

    [RelayCommand]
    private void CaptureWindows()
    {
        var newWindowLayers = new ObservableCollection<WindowLayer>();
        EnumWindows((hWnd,
                     lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var windowLayer = CaptureWindow(hWnd);
            if (windowLayer is not null)
            {
                newWindowLayers.Add(windowLayer);
            }

            return true;
        }, IntPtr.Zero);

        WindowLayers = newWindowLayers;
    }

    [RelayCommand]
    private void SaveImages()
    {
        var folderPath = Path.Combine("C:", "temp", "Quarkshot");
        CleanFolder(folderPath);
        Directory.CreateDirectory(folderPath);

        foreach (var layer in WindowLayers)
        {
            var       sanitizedTitle = SanitizeFileName(layer.Title);
            var       fileName       = GetUniqueFileName(folderPath, sanitizedTitle, ".png");
            var       filePath       = Path.Combine(folderPath, fileName);
            using var fileStream     = new FileStream(filePath, FileMode.Create);
            layer.Image.Save(fileStream);
        }
    }

    private static void CleanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        foreach (var file in Directory.GetFiles(folderPath))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Can't be arsed to implement in POC
            }
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars  = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
        return Regex.Replace(fileName, invalidRegStr, "_");
    }

    private static string GetUniqueFileName(string folderPath,
                                            string fileName,
                                            string extension)
    {
        var fileNameOnly = Path.GetFileNameWithoutExtension(fileName);
        var fullPath     = Path.Combine(folderPath, fileName + extension);

        var count = 1;
        while (File.Exists(fullPath))
        {
            var tempFileName = string.Format("{0}({1})", fileNameOnly, count++);
            fullPath = Path.Combine(folderPath, tempFileName + extension);
        }

        return Path.GetFileName(fullPath);
    }

    private static WindowLayer? CaptureWindow(IntPtr hWnd)
    {
        GetWindowRect(hWnd, out var rect);

        var width  = rect.Right  - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            return null;

        var hdcScreen = GetDC(IntPtr.Zero);
        var hdcMemDc  = CreateCompatibleDC(hdcScreen);
        var hBitmap   = CreateCompatibleBitmap(hdcScreen, width, height);
        var hOld      = SelectObject(hdcMemDc, hBitmap);

        try
        {
            // Use PrintWindow with PW_RENDERFULLCONTENT flag
            if (!PrintWindow(hWnd, hdcMemDc, 0x00000002))
            {
                // PrintWindow failed, fall back to BitBlt
                BitBlt(hdcMemDc, 0, 0, width, height, hdcScreen, rect.Left, rect.Top, 0x00CC0020);
            }

            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888,
                                             AlphaFormat.Premul);

            using (var lockedBitmap = bitmap.Lock())
            {
                var bmi = new Bitmapinfo
                {
                    bmiHeader = new Bitmapinfoheader
                    {
                        biSize        = (uint)Marshal.SizeOf<Bitmapinfoheader>(),
                        biWidth       = width,
                        biHeight      = -height, // Top-down bitmap
                        biPlanes      = 1,
                        biBitCount    = 32,
                        biCompression = 0, // BI_RGB
                    }
                };

                unsafe
                {
                    if (GetDIBits(hdcMemDc, hBitmap, 0, (uint)height, lockedBitmap.Address, ref bmi, 0) == 0)
                    {
                        throw new Exception("Failed to get bitmap bits.");
                    }

                    // Convert BGRA to RGBA if necessary
                    var destPtr    = (byte*)lockedBitmap.Address;
                    var destStride = lockedBitmap.RowBytes;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var pixelOffset = y * destStride + x * 4;
                            (destPtr[pixelOffset], destPtr[pixelOffset + 2])
                                = (destPtr[pixelOffset                 + 2], destPtr[pixelOffset]);
                        }
                    }
                }
            }

            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            var title = sb.ToString();

            return new WindowLayer { Hwnd = hWnd, Image = bitmap, Title = title };
        }
        finally
        {
            SelectObject(hdcMemDc, hOld);
            DeleteDC(hdcMemDc);
            ReleaseDC(IntPtr.Zero, hdcScreen);
            DeleteObject(hBitmap);
        }
    }

    // Additional P/Invoke declarations
    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr           hdc,
                                IntPtr           hbmp,
                                uint             uStartScan,
                                uint             cScanLines,
                                [Out] IntPtr     lpvBits,
                                ref   Bitmapinfo lpbmi,
                                uint             uUsage);

    [StructLayout(LayoutKind.Sequential)]
    struct Bitmapinfoheader
    {
        public uint   biSize;
        public int    biWidth;
        public int    biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint   biCompression;
        public uint   biSizeImage;
        public int    biXPelsPerMeter;
        public int    biYPelsPerMeter;
        public uint   biClrUsed;
        public uint   biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Bitmapinfo
    {
        public Bitmapinfoheader bmiHeader;
        public int[]            bmiColors;
    }

    // P/Invoke declarations
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc enumProc,
                                   IntPtr          lParam);

    [DllImport("user32.dll")]
    static extern int GetWindowText(IntPtr        hWnd,
                                    StringBuilder lpString,
                                    int           nMaxCount);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr   hWnd,
                                     out Rect lpRect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd,
                                   IntPtr hdcBlt,
                                   uint   nFlags);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc,
                                                int    nWidth,
                                                int    nHeight);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc,
                                      IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd,
                                IntPtr hDc);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest,
                              int    nXDest,
                              int    nYDest,
                              int    nWidth,
                              int    nHeight,
                              IntPtr hdcSrc,
                              int    nXSrc,
                              int    nYSrc,
                              uint   dwRop);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    delegate bool EnumWindowsProc(IntPtr hWnd,
                                  IntPtr lParam);
}