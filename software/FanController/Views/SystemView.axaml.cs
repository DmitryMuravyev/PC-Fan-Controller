////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SystemView.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.Converters;
using FanController.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;


namespace FanController.Views;

/// <summary>
/// System Tab contents View
/// </summary>
public partial class SystemView : UserControl
{
    // Serialization options for config backup/restore
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,                           // Human-readable JSON
        PropertyNameCaseInsensitive = true,             // Robust on load
        Converters = { new JsonStringEnumConverter() }  // Enums as strings
    };


    // View Model object for direct accessing it's properties.
    private SystemViewModel? _viewModel = null;

    // Suppresses ComboBox.SelectionChanged side effects while columns are being rebuilt
    private bool _isRecreatingColumns = false;

    // Previously opened folder
    private IStorageFolder? _lastUsedFolder = null;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public SystemView()
    {
        InitializeComponent();

        // The previously used method for limiting the sending
        // of multiple commands when moving the slider.
        //DisplayBrightnessSlider.AddHandler(CompactSlider.ThumbDragStartedEvent, OnSliderDragStarted);
        //DisplayBrightnessSlider.AddHandler(CompactSlider.ThumbDragCompletedEvent, OnSliderDragCompleted);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    /// <summary>
    /// Tracking the assignment of the initialized ViewModel object to the DataContext.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from the previous ViewModel to avoid leaking handlers
        if (_viewModel is not null)
        {
            _viewModel.SubstitutionMatrixRecreateColumns -= RecreateColumnsInMainThread;
            _viewModel.CommandCompleted -= OnCommandCompleted;
        }

        _viewModel = DataContext as SystemViewModel;

        if (_viewModel is not null)
        {
            _viewModel.SubstitutionMatrixRecreateColumns += RecreateColumnsInMainThread;
            _viewModel.CommandCompleted += OnCommandCompleted;
            CreateColumns();
        }
    }


    // Reports the device errors of the commands awaited in the background
    private async void OnCommandCompleted(object? sender, CommandCompletedEventArgs e)
    {
        if (e.Outcome != CommandOutcome.Failed) return;

        // The tab may not be displayed: fall back to the main window
        Window? window = (TopLevel.GetTopLevel(this) as Window) ??
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (window is not null)
            await ShowCommandErrorMessage(window);
    }


    /// <summary>
    /// Looks up a themed resource by key, optionally extracting a specific
    /// setter value from a <see cref="ControlTheme"/> (e.g. default FontSize).
    /// Falls back to <paramref name="defaultValue"/> if not found.
    /// </summary>
    private T GetResource<T>(object key, AvaloniaProperty? property, T defaultValue)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var resource) != true
            || resource is null)
            return defaultValue;

        if (property is null)
            return resource is T direct ? direct : defaultValue;

        if (resource is ControlTheme theme)
        {
            var setter = theme.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == property);
            if (setter?.Value is T value)
                return value;
        }

        return defaultValue;
    }


    /// <summary>
    /// Resolves the owning <see cref="Window"/> and current ViewModel.
    /// Returns false (and sets both out params to null) if either is unavailable.
    /// </summary>
    private bool TryGetWindowAndViewModel(
        [NotNullWhen(true)] out Window? window,
        [NotNullWhen(true)] out SystemViewModel? viewModel)
    {
        window = TopLevel.GetTopLevel(this) as Window;
        viewModel = _viewModel;
        return ((window is not null) && (viewModel is not null));
    }


    /// <summary>
    /// Displays a dialog box with a generic command execution error message. 
    /// </summary>
    private Task<bool?> ShowCommandErrorMessage(Window window)
    {
        var errorDialog = new ConfirmDialog(
            Strings.DialogCommandErrorHeaderFormat,
            Strings.DialogCommandErrorMessageFormat,
            Strings.ButtonOkCaption,
            Strings.DialogErrorTitle,
            true);

        return errorDialog.ShowDialog<bool?>(window);
    }


    /// <summary>
    /// Asks for confirmation, executes the command and reports a failure.
    /// </summary>
    private async Task ConfirmAndExecuteAsync(
        string header,
        string message,
        Func<SystemViewModel, Task<bool>> command,
        string? confirmCaption = null)
    {
        if (!TryGetWindowAndViewModel(out var window, out var viewModel)) return;

        var dialog = new ConfirmDialog(header, message, confirmCaption);
        if (await dialog.ShowDialog<bool?>(window) != true) return;

        if (!await command(viewModel))
            await ShowCommandErrorMessage(window);
    }

    ////////////////////////////////////////////////////////////////////////////////////////
    // Substitution matrix column management 

    private void RecreateColumnsInMainThread()
    {
        /*
        // For future use
        Dispatcher.UIThread.Post(() =>
        {
            CreateColumns();
        });
        */
        CreateColumns();
    }


    /// <summary>
    /// Create substitution table columns.
    /// </summary>
    private void CreateColumns()
    {
        if (SubstitutionMatrixDataGrid != null)
        {
            _isRecreatingColumns = true;
            try
            {
                // Clear previous columns
                SubstitutionMatrixDataGrid.Columns.Clear();

                // Get header cell style for the first column cells.
                int fansCount = (_viewModel != null) ? _viewModel.FanCount : 0;
                object headerBackground = GetResource(
                    "DataGridColumnHeaderBackgroundBrush", 
                    null, 
                    new SolidColorBrush(Color.Parse("#FFFFFFFF")));
                object headerForeground = GetResource(
                    "DataGridColumnHeaderForegroundBrush", 
                    null, 
                    new SolidColorBrush(Color.Parse("#9900FF00")));
                object headerLines = GetResource(
                    "DataGridGridLinesBrush", 
                    null, 
                    new SolidColorBrush(Color.Parse("#66000000"), 0.4));
                object fontSize = GetResource(
                    typeof(DataGridColumnHeader), 
                    TextBlock.FontSizeProperty, 
                    12.0);

                // First column: row headers showing the fan name being substituted
                var headerColumn = new DataGridTemplateColumn
                {
                    Header = (fansCount > 0) ? "#\\#" : Strings.ColumnHeaderNoFansCaption,
                    CellTemplate = new FuncDataTemplate<object>((item, nameScope) =>
                        new Border
                        {
                            Background = (IBrush)headerBackground,
                            //Padding = new Thickness(12, 0, 0, 0),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            BorderBrush = (IBrush)headerLines,
                            Child = new TextBlock
                            {
                                FontSize = (double)fontSize!,
                                //FontWeight = FontWeight.Light,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                Foreground = (IBrush)headerForeground,

                                [!TextBlock.TextProperty] = new Binding("FanName")
                                {
                                    Mode = BindingMode.OneWay
                                }
                            }
                        })
                };

                SubstitutionMatrixDataGrid.Columns.Add(headerColumn);

                // One column per fan — each cell is a ComboBox selecting a substitution coefficient
                for (int i = 0; i < fansCount; i++)
                {
                    // Create a local copy for this iteration (for passing it to the lambda)
                    int columnIndex = i;

                    // Header cell template
                    var headerTemplate = new FuncDataTemplate<object>((_, _) =>
                    {
                        var textBlock = new TextBlock();
                        var binding = new Binding($"SubstitutionMatrix[{columnIndex}].FanName")
                        {
                            Source = _viewModel,
                            Mode = BindingMode.OneWay
                        };
                        textBlock.Bind(TextBlock.TextProperty, binding);
                        return textBlock;
                    });

                    var coefficientsColumn = new DataGridTemplateColumn
                    {
                        Width = new DataGridLength(100),
                        HeaderTemplate = headerTemplate,
                        CellTemplate = new FuncDataTemplate<SubstitutionVector>((item, scope) =>
                        {
                            var comboBox = new ComboBox
                            {
                                ItemsSource = _viewModel!.AllSubstitutionCoefficients,
                                ItemTemplate = new FuncDataTemplate<object>((enumItem, _) =>
                                {
                                    var textBlock = new TextBlock();

                                    var multiBinding = new MultiBinding
                                    {
                                        Converter = EnumToLocalizedTextConverter.Instance,
                                        Mode = BindingMode.OneWay
                                    };

                                    // Binding to the Enum object itself
                                    multiBinding.Bindings.Add(new Binding("."));

                                    textBlock.Bind(TextBlock.TextProperty, multiBinding);
                                    return textBlock;
                                }),
                                Margin = new Thickness(0),
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                                BorderThickness = new Thickness(0),
                                CornerRadius = new CornerRadius(0),
                                Tag = columnIndex
                            };

                            // Binding to the element of array
                            var selectedIndexBinding = new Binding($"SubstitutionCoefficients[{columnIndex}]")
                            {
                                Mode = BindingMode.TwoWay
                            };
                            comboBox.Bind(ComboBox.SelectedIndexProperty, selectedIndexBinding);

                            comboBox.SelectionChanged += (sender, e) =>
                            {
                                // Checking that the list of added elements is not empty
                                if (e.AddedItems.Count > 0)
                                {
                                    // Getting the newly selected value
                                    //var oldValue = e.RemovedItems[0];
                                    //var newValue = e.AddedItems[0];

                                    if ((_viewModel is null) || _viewModel.SMHardwareUpdating) return;

                                    var cb = (ComboBox)sender!;
                                    if (!cb.IsLoaded) return;
                                    if (e.RemovedItems.Count == 0) return;
                                    if (_isRecreatingColumns) return;

                                    int colIndex = (int)cb.Tag!; // Getting the saved column index
                                    var rowData = cb.DataContext as SubstitutionVector;

                                    if (rowData != null)
                                    {
                                        int rowIndex = _viewModel.SubstitutionMatrix.IndexOf(rowData);
                                        _viewModel.CheckSMUserChanges(rowIndex, colIndex);
                                    }
                                    else
                                    {
                                        _viewModel.IsDirtySM = true;
                                    }
                                }
                            };

                            // Disable the ComboBox for the diagonal cell (fan substituting itself)
                            var disableStyle = new Style(x => x.OfType<ComboBox>().PropertyEquals(ComboBox.SelectedIndexProperty, -1));
                            disableStyle.Setters.Add(new Setter(InputElement.IsEnabledProperty, false));
                            comboBox.Styles.Add(disableStyle);

                            return comboBox;
                        })
                    };

                    SubstitutionMatrixDataGrid.Columns.Add(coefficientsColumn);
                }
            }
            finally
            {
                // Using a short delay to allow the events to pass through
                Dispatcher.UIThread.Post(() => _isRecreatingColumns = false, DispatcherPriority.Loaded);
            }
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons

    private async void OnSaveConfigClick(object? sender, RoutedEventArgs e) =>
        await ConfirmAndExecuteAsync(
            Strings.DialogSystemConfigNotSavedHeaderFormat,
            Strings.DialogConfigNotSavedConfirmMessageFormat,
            viewModel => viewModel.UpdateConfiguration(),
            Strings.ButtonSaveCaption);


    private async void OnFactoryResetClick(object? sender, RoutedEventArgs e) =>
        await ConfirmAndExecuteAsync(
            Strings.DialogFactoryResetControllerHeaderFormat,
            Strings.DialogFactoryResetControllerMessageFormat,
            viewModel => viewModel.FactoryResetController());


    private async void OnRebootClick(object? sender, RoutedEventArgs e) =>
        await ConfirmAndExecuteAsync(
            Strings.DialogRebootControllerHeaderFormat,
            Strings.DialogRebootControllerMessageFormat,
            viewModel => viewModel.RebootController());


    private async void OnBackupConfigClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetWindowAndViewModel(out var window, out var viewModel)) return;
        if (viewModel.SelectedDeviceId is null) return;

        IStorageFolder? startingFolder = _lastUsedFolder
            ?? await window.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
        string assemblyName = typeof(App).Assembly.GetName().Name ?? "FanController";

        var file = await window!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ConfigurationSaveWindow,
            SuggestedFileName = $"{assemblyName}.json",
            SuggestedStartLocation = startingFolder,
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
            new FilePickerFileType(Strings.ConfigurationJsonFileFormatShortName) { Patterns = new[] { "*.json" } }
        }
        });

        if (file is null) return;
        
        _lastUsedFolder = await file.GetParentAsync();

        // Fetch the config BEFORE creating the file
        DeviceFullConfig? config = await viewModel.GetFullConfiguration();

        if (config is null)
        {
            var errorDialog = new ConfirmDialog(
                Strings.DialogEmptyConfigurationHeaderFormat,
                Strings.DialogEmptyConfigurationMessageFormat,
                Strings.ButtonOkCaption,
                Strings.DialogErrorTitle,
                true);

            await errorDialog.ShowDialog<bool?>(window);
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, config, Options);
        }
        catch (Exception ex)
        {
            var errorDialog = new ConfirmDialog(
                Strings.DialogErrorWritingConfigurationFileHeaderFormat,
                ex.Message,
                Strings.ButtonOkCaption,
                Strings.DialogErrorTitle,
                true);

            await errorDialog.ShowDialog<bool?>(window);
        }
    }


    private async void OnRestoreConfigClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetWindowAndViewModel(out var window, out var viewModel)) return;
        if (viewModel.SelectedDeviceId is null) return;

        IStorageFolder? startingFolder = _lastUsedFolder;
        if (startingFolder is null) startingFolder =
                await window!.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

        var files = await window!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ConfigurationLoadWindow,
            SuggestedStartLocation = startingFolder,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(
                    Strings.ConfigurationJsonFileFormatShortName) { Patterns = new[] { "*.json" } },
                new FilePickerFileType(
                    Strings.ConfigurationAllFileFormatsShortName) { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 1)
        {
            _lastUsedFolder = await files[0].GetParentAsync();
            await using var stream = await files[0].OpenReadAsync();
            try
            {
                DeviceFullConfig? config = 
                    await JsonSerializer.DeserializeAsync<DeviceFullConfig>(stream, Options);
                if (config is null) 
                    throw new InvalidOperationException(Strings.DialogBadConfigurationFileMessageFormat);

                if (!await viewModel.UploadFullConfiguration(config))
                    await ShowCommandErrorMessage(window);
            }
            catch ( Exception ex )
            {
                var dialog = new ConfirmDialog(
                    Strings.DialogBadConfigurationFileHeaderFormat,
                    ex.Message,
                    Strings.ButtonOkCaption,
                    Strings.DialogErrorTitle,
                    true);

                await dialog.ShowDialog<bool?>(window);
            }
        }
    }


    private async void OnCustomizeBuzzerOptionsClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetWindowAndViewModel(out var window, out var viewModel)) return;

        var details = new BuzzerOptionsWindow(viewModel);

        // We don't need any return value here
        await details.ShowDialog<object>(window);

        /*
        if (updated is not null)
        {
            fan.UpdateConfiguration(updated);
        }
        */
    }


    // Layout performance diagnostics
#if DEBUG
    protected override Size MeasureOverride(Size availableSize)
    {
        Stopwatch stopWatch = Stopwatch.StartNew();
        var result = base.MeasureOverride(availableSize);
        Debug.WriteLine($"Measure Time: {stopWatch.Elapsed.TotalMilliseconds} ms");
        return result;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Stopwatch stopWatch = Stopwatch.StartNew();
        var result = base.ArrangeOverride(finalSize);
        Debug.WriteLine($"Arrange Time: {stopWatch.Elapsed.TotalMilliseconds} ms");
        return result;
    }
#endif

}

// END-OF-FILE
