////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// LocalizationService.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Assets.Localization;


namespace FanController.Localization;

/// <summary>
/// Localization helper: localized strings by key for XAML bindings and code.
/// <see cref="SetCulture"/> switches the language at runtime: all
/// {lz:Localize} bindings refresh themselves; texts built in code
/// have to handle <see cref="CultureChanged"/>.
/// </summary>
public sealed class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    // Raised after the culture has been changed
    public event EventHandler? CultureChanged;


    private LocalizationService() { }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    /// <summary>
    /// Localized string. A missing key is returned as "[key]",
    /// so missing translations are visible in the UI.
    /// </summary>
    public string this[string key]
    {
        get
        {
            string? value = Strings.ResourceManager.GetString(key, Strings.Culture);
            if (value is not null) return value;

            Debug.WriteLine($"[Localization] Missing string: '{key}'.");
            return $"[{key}]";
        }
    }


    // Culture of the strings (the OS UI culture until one is set)
    public CultureInfo CurrentCulture => Strings.Culture ?? CultureInfo.CurrentUICulture;


    /// <summary>
    /// Switches the UI language and the number/date formatting.
    /// </summary>
    /// <exception cref="CultureNotFoundException">Unknown culture name.</exception>
    public void SetCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        Strings.Culture = culture;

        // The current thread and all threads started later (thread pool included)
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        // "Item[]" refreshes all indexer bindings ({lz:Localize}, [Key] paths)
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(CurrentCulture));

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}


/// <summary>
/// {lz:Localize Key}: a one-way binding to the localized string
/// (refreshed when the culture changes).
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; }

    public LocalizeExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        };
}

// END-OF-FILE
