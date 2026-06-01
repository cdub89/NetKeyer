using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using NetKeyer.Helpers;
using NetKeyer.ViewModels;

namespace NetKeyer.Views;

public partial class MainWindow : Window
{
    private bool _geometryRestored;
    private bool _currentIsOperating;    // which page the view currently tracks as active
    private double _defaultWidth = 480;  // XAML default; used when a page has no saved width

    public MainWindow()
    {
        InitializeComponent();

        // Window geometry persistence (width + position + maximized; height auto-sizes).
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        PositionChanged += (_, _) => CaptureGeometry();

        // Set up native macOS menu bar
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetupMacOsNativeMenu();
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        // The XAML width is the per-page default when a page has no saved width yet.
        _defaultWidth = Width;

        if (DataContext is MainWindowViewModel vm)
        {
            _currentIsOperating = vm.IsOperatingPage;   // Setup at startup
            ApplyPageGeometry(vm, _currentIsOperating);
            if (vm.SavedWindowMaximized)
                WindowState = WindowState.Maximized;

            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ResetWindowRequested += OnResetWindowRequested;
        }
        // Only start capturing after restore so we don't overwrite saved values with defaults.
        _geometryRestored = true;
        GeomLog("opened", $"restored, operating={_currentIsOperating}");
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CurrentPage)) return;
        if (DataContext is not MainWindowViewModel vm) return;

        bool newIsOperating = vm.IsOperatingPage;
        if (newIsOperating == _currentIsOperating) return;

        // Save the OUTGOING page's geometry to its own slots, then switch and apply the
        // INCOMING page's geometry. Routing by the view's own page flag (updated here in
        // lockstep) keeps each page independent regardless of capture-event timing.
        if (_geometryRestored && WindowState == WindowState.Normal)
        {
            vm.StorePageWidth(_currentIsOperating, Width);
            vm.StorePagePosition(_currentIsOperating, Position.X, Position.Y);
        }

        _currentIsOperating = newIsOperating;
        ApplyPageGeometry(vm, _currentIsOperating);
        GeomLog("page-switch", $"now {(newIsOperating ? "Operating" : "Setup")}");
    }

    // Apply a page's saved width + position. Width falls back to the default so a page never
    // inherits the other page's width; position is left as-is if that page has none stored yet.
    private void ApplyPageGeometry(MainWindowViewModel vm, bool operating)
    {
        double? savedW = vm.GetPageWidth(operating);
        double targetW = (savedW is double w && w >= MinWidth && w <= 4000) ? w : _defaultWidth;
        Width = targetW;

        if (vm.GetPageLeft(operating) is int l && vm.GetPageTop(operating) is int t)
            Position = new PixelPoint(l, t);

        GeomLog("apply", $"{(operating ? "Operating" : "Setup")} W={targetW} pos=({vm.GetPageLeft(operating)},{vm.GetPageTop(operating)})");
    }

    // Reset Windows: the view model has already cleared the saved geometry; restore the live
    // window to the default size and a centered, system-style position.
    private void OnResetWindowRequested(object? sender, EventArgs e)
    {
        _geometryRestored = false;  // suppress capture while resetting so settings stay cleared
        WindowState = WindowState.Normal;
        Width = _defaultWidth;
        try
        {
            var screen = Screens?.Primary;
            if (screen is not null)
            {
                var area = screen.WorkingArea;
                double scaling = screen.Scaling;
                int wpx = (int)(Width * scaling);
                int hpx = (int)(Bounds.Height * scaling);
                Position = new PixelPoint(
                    area.X + System.Math.Max(0, (area.Width - wpx) / 2),
                    area.Y + System.Math.Max(0, (area.Height - hpx) / 2));
            }
        }
        catch { /* best-effort centering */ }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _geometryRestored = true);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        CaptureGeometry();
        if (DataContext is MainWindowViewModel vm)
            vm.SaveSettings();
        GeomLog("closing", "captured + saved");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == WindowStateProperty)
            CaptureGeometry();
    }

    // Keep the in-memory geometry current. Width is routed to the page the view is tracking;
    // position/maximized are shared. Width/position captured only while in the Normal state.
    private void CaptureGeometry()
    {
        if (!_geometryRestored) return;
        if (DataContext is not MainWindowViewModel vm) return;

        bool maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            vm.StorePageWidth(_currentIsOperating, Width);
            vm.StorePagePosition(_currentIsOperating, Position.X, Position.Y);
            GeomLog("capture", $"{(_currentIsOperating ? "Operating" : "Setup")} Width={Width} pos=({Position.X},{Position.Y})");
        }
        vm.StoreMaximized(maximized);
    }

    private static void GeomLog(string evt, string detail)
    {
        if (DebugLogger.IsEnabled("geometry"))
            DebugLogger.Log("geometry", $"{evt}: {detail}");
    }

    private void SetupMacOsNativeMenu()
    {
        // Create the native menu for macOS
        var nativeMenu = new NativeMenu();
        
        // File menu
        var fileMenu = new NativeMenuItem("File");
        var fileSubMenu = new NativeMenu();
        
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ExitCommand?.Execute(null);
            }
        };
        fileSubMenu.Add(exitItem);
        fileMenu.Menu = fileSubMenu;

        // Settings menu
        var settingsMenu = new NativeMenuItem("Settings");
        var settingsSubMenu = new NativeMenu();

        var audioDeviceItem = new NativeMenuItem("Audio Output Device...");
        audioDeviceItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectAudioDeviceCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(audioDeviceItem);

        var midiNoteMappingItem = new NativeMenuItem("MIDI Note Mapping...");
        midiNoteMappingItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConfigureMidiNotesCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(midiNoteMappingItem);

        var resetWindowsItem = new NativeMenuItem("Reset Windows");
        resetWindowsItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ResetWindowsCommand?.Execute(null);
            }
        };
        settingsSubMenu.Add(new NativeMenuItemSeparator());
        settingsSubMenu.Add(resetWindowsItem);

        settingsMenu.Menu = settingsSubMenu;

        // Help menu
        var helpMenu = new NativeMenuItem("Help");
        var helpSubMenu = new NativeMenu();
        
        var documentationItem = new NativeMenuItem("Documentation");
        documentationItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OpenDocumentationCommand?.Execute(null);
            }
        };
        
        var aboutItem = new NativeMenuItem("About NetKeyer...");
        aboutItem.Click += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ShowAboutCommand?.Execute(null);
            }
        };
        
        helpSubMenu.Add(documentationItem);
        helpSubMenu.Add(aboutItem);
        helpMenu.Menu = helpSubMenu;
        
        // Add menus to the native menu bar
        nativeMenu.Add(fileMenu);
        nativeMenu.Add(settingsMenu);
        nativeMenu.Add(helpMenu);
        
        // Set the native menu for this window
        NativeMenu.SetMenu(this, nativeMenu);
    }
}