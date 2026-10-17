////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ConfirmDialog.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;


namespace FanController.Views;

/// <summary>
/// Universal dialog box: a confirmation (Continue / Cancel), or a message
/// with a single button. ShowDialog&lt;bool?&gt; returns true if confirmed,
/// false if cancelled (also by Escape).
/// </summary>
public partial class ConfirmDialog : Window
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    /// <summary>
    /// Default constructor for the XAML loader and the design previewer.
    /// </summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the dialog.
    /// </summary>
    /// <param name="header">Bold header text.</param>
    /// <param name="message">Message text.</param>
    /// <param name="confirmButtonCaption">Confirmation button caption (default: Continue).</param>
    /// <param name="title">Window title (default: Confirm).</param>
    /// <param name="hideCancelButton">Message mode: the confirmation button only.</param>
    public ConfirmDialog(
        string header,
        string message,
        string? confirmButtonCaption = null,
        string? title = null,
        bool hideCancelButton = false) : this()
    {
        HeaderText.Text = header;
        MessageText.Text = message;

        if (confirmButtonCaption is not null) ContinueButton.Content = confirmButtonCaption;
        if (title is not null) Title = title;
        CancelButton.IsVisible = !hideCancelButton;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Event handlers

    // Escape cancels, also when the Cancel button is hidden
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.Handled && (e.Key == Key.Escape))
        {
            e.Handled = true;
            Close(false);
        }
    }


    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }


    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

// END-OF-FILE