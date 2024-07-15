using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Quarkshot.Models;

public partial class WindowLayer : ObservableObject
{
    [ObservableProperty]
    private IntPtr _hwnd;

    [ObservableProperty]
    private WriteableBitmap _image;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _zOrder;
}