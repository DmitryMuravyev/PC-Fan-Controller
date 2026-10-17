////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// MainWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia.Controls;
using Avalonia.Interactivity;
using FanController.ViewModels;
using System;


namespace FanController.Views;

/// <summary>
/// Main application window
/// </summary>
public partial class MainWindow : Window
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public MainWindow()
    {
        InitializeComponent();
    }


    // The window must fit the screen (e.g. 768 px high laptop screens, display scaling)
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowScreenFit.FitToScreen(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Custom title bar

    // For the Windows OS we can use TitleBar to display custom menu, overall state color and so on...
    // Since this project is cross-platform, I will not use this feature, but I will save the code for the future.
    /*
    // Drag Window by the TitleBar
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    // Minimize/maximize Window by double-click
    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized)
            ? WindowState.Normal
            : WindowState.Maximized;
    }
    */


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons

    private async void OnConfigureHostsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        using var hostListViewModel = viewModel.CreateHostListViewModel();
        await new HostListWindow(hostListViewModel).ShowDialog(this);
    }

}

// END-OF-FILE
