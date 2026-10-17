////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostDetailsViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Assets.Localization;
using FanController.Settings;


namespace FanController.ViewModels;

/// <summary>
/// Host details window View Model: adding a new host or editing an existing one.
/// </summary>
public partial class HostDetailsViewModel : ObservableValidator
{
    private readonly HostListViewModel _parent;

    // Snapshot kept for the dirty-check (a new configuration in the Add mode)
    public HostConfig Original { get; }

    // Window mode: adding a new host or editing an existing one
    public bool IsAddWindow { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Editable parameters

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(HostDetailsViewModel), nameof(ValidateNotNullOrWhiteSpace))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _name;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(HostDetailsViewModel), nameof(ValidateNotNullOrWhiteSpace))]
    [CustomValidation(typeof(HostDetailsViewModel), nameof(ValidateHostAddress))]
    [CustomValidation(typeof(HostDetailsViewModel), nameof(ValidateEndpointUniqueness))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _host;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 65535)]
    [CustomValidation(typeof(HostDetailsViewModel), nameof(ValidateEndpointUniqueness))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private int _port;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public HostDetailsViewModel(HostListViewModel parent, HostConfig? existing)
    {
        _parent = parent;
        IsAddWindow = existing is null;
        Original = existing ?? new HostConfig
        {
            Id = Guid.NewGuid(),
            Name = String.Empty,
            Host = String.Empty,
            Port = SettingsDefaults.DefaultDaemonPort
        };

        _name = Original.Name;
        _host = Original.Host;
        _port = Original.Port;

        ErrorsChanged += (_, _) => OnPropertyChanged(nameof(IsValid));

        // An existing configuration is checked right away; a new one starts empty
        // (no errors before the first edit, but IsValid keeps Save disabled)
        if (!IsAddWindow) ValidateAllProperties();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    // No validation errors and the required fields are filled in
    public bool IsValid =>
        !HasErrors &&
        !String.IsNullOrWhiteSpace(Name) &&
        !String.IsNullOrWhiteSpace(Host);


    // Compared as saved (trimmed)
    public bool IsDirty =>
        (Name.Trim() != Original.Name) ||
        (Host.Trim() != Original.Host) ||
        (Port != Original.Port);


    public HostConfig BuildConfig() => new()
    {
        Id = Original.Id,
        Name = Name.Trim(),
        Host = Host.Trim(),
        Port = Port
    };


    ////////////////////////////////////////////////////////////////////////////////////////
    // Validation

    public static ValidationResult? ValidateNotNullOrWhiteSpace(object? value, ValidationContext ctx) =>
        ((value is string text) && !String.IsNullOrWhiteSpace(text))
            ? ValidationResult.Success
            : new ValidationResult(Strings.ValidationFieldCannotBeEmptyFormat);


    // Host name, IPv4 or IPv6 address (emptiness is reported by ValidateNotNullOrWhiteSpace)
    public static ValidationResult? ValidateHostAddress(object? value, ValidationContext ctx)
    {
        if ((value is not string host) || String.IsNullOrWhiteSpace(host))
            return ValidationResult.Success;

        return (Uri.CheckHostName(host.Trim()) != UriHostNameType.Unknown)
            ? ValidationResult.Success
            : new ValidationResult(Strings.ValidationInvalidHostAddressFormat);
    }


    public static ValidationResult? ValidateEndpointUniqueness(object? value, ValidationContext ctx)
    {
        if ((ctx.ObjectInstance is not HostDetailsViewModel vm) || String.IsNullOrWhiteSpace(vm.Host))
            return ValidationResult.Success;

        return vm._parent.HostEndpointExists(vm.Host, vm.Port, vm.Original.Id)
            ? new ValidationResult(
                Strings.ValidationEndpointAlreadyExistsFormat,
                new[] { nameof(Host), nameof(Port) })
            : ValidationResult.Success;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changed handlers

    // Cross-validate the endpoint when either part changes
    partial void OnHostChanged(string value) =>
        ValidateProperty(Port, nameof(Port));

    partial void OnPortChanged(int value) =>
        ValidateProperty(Host, nameof(Host));

}

// END-OF-FILE
