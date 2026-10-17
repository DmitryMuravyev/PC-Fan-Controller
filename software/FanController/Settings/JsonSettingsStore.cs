////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// JsonSettingsStore.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


namespace FanController.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON in the per-user local data folder:
///   Windows : %LOCALAPPDATA%\FanController\settings.json
///   Linux   : ~/.local/share/FanController/settings.json
///   macOS   : ~/Library/Application Support/FanController/settings.json
/// Writes go through a temporary file that replaces the target, so a crash during
/// saving can't leave a truncated file. A file that can't be read is kept as
/// settings.json.bak and replaced by defaults.
/// </summary>
public sealed class JsonSettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Be forgiving if the settings file is edited by hand
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _stateLock = new();

    private AppSettings _settings = new();
    private volatile bool _isLoaded;
    private Task? _loadTask;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public JsonSettingsStore()
    {
        string assemblyName = typeof(App).Assembly.GetName().Name ?? "FanController";

        // We can also use the
        // Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        // for 'Roaming' (not the 'Local') subfolder.
        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            assemblyName);

        Directory.CreateDirectory(configDir);
        _filePath = Path.Combine(configDir, FileName);

        Debug.WriteLine($"[Settings] File path: {_filePath}");
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    /// <summary>
    /// The current in-memory settings. Prefer <see cref="UpdateAsync"/> for changes;
    /// after mutating this instance directly, call <see cref="SaveAsync"/>.
    /// </summary>
    public AppSettings Settings
    {
        get
        {
            lock (_stateLock)
            {
                return _settings;
            }
        }
    }


    /// <summary>
    /// Loads the settings synchronously (once). Prefer <see cref="EnsureLoadedAsync"/> in async code.
    /// </summary>
    public void Load()
    {
        if (_isLoaded) return;

        lock (_stateLock)
        {
            if (_isLoaded) return;

            AppSettings? loaded = null;
            if (File.Exists(_filePath))
            {
                try
                {
                    loaded = Parse(File.ReadAllText(_filePath));
                }
                catch (Exception ex)
                {
                    LogLoadFailure(ex);
                    BackupBrokenFile();
                }
            }

            if (loaded is null)
            {
                loaded = CreateDefaults(out string json);
                try
                {
                    AtomicWrite(json);
                }
                catch (Exception ex)
                {
                    LogSaveFailure(ex);
                }
            }

            _settings = loaded;
            _isLoaded = true;
        }
    }


    /// <summary>
    /// Ensures the settings are loaded. Safe to call multiple times (loads once).
    /// </summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded) return Task.CompletedTask;

        lock (_stateLock)
        {
            if (_isLoaded) return Task.CompletedTask;

            // Return the in-flight load, if any
            return _loadTask ??= LoadInternalAsync(cancellationToken);
        }
    }


    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        EnsureLoadedAsync(cancellationToken);


    /// <summary>
    /// Applies a change under the lock and saves the settings.
    /// Returns false if the file could not be written (the change stays in memory).
    /// </summary>
    public async Task<bool> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await EnsureLoadedAsync(cancellationToken);

        lock (_stateLock)
        {
            updateAction(_settings);
        }

        return await SaveAsync(cancellationToken);
    }


    /// <summary>
    /// Writes the current settings to disk. Returns false if writing failed (logged).
    /// </summary>
    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        // Serialize concurrent saves
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            string json;
            lock (_stateLock)
            {
                json = JsonSerializer.Serialize(_settings, JsonOptions);
            }

            await AtomicWriteAsync(json, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailure(ex);
            return false;
        }
        finally
        {
            _saveLock.Release();
        }
    }


    /// <summary>
    /// Reloads the settings from disk, discarding unsaved in-memory changes.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            lock (_stateLock)
            {
                _isLoaded = false;
                _loadTask = null;
            }

            await EnsureLoadedAsync(cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Loading

    private async Task LoadInternalAsync(CancellationToken cancellationToken)
    {
        AppSettings? loaded = null;

        if (File.Exists(_filePath))
        {
            try
            {
                loaded = Parse(await File.ReadAllTextAsync(_filePath, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLoadFailure(ex);
                BackupBrokenFile();
            }
        }

        if (loaded is null)
        {
            loaded = CreateDefaults(out string json);
            try
            {
                await AtomicWriteAsync(json, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSaveFailure(ex);
            }
        }

        lock (_stateLock)
        {
            _settings = loaded;
            _isLoaded = true;
        }
    }


    // Throws JsonException for an empty or invalid file
    private static AppSettings Parse(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
            ?? throw new JsonException("The settings file is empty.");


    private static AppSettings CreateDefaults(out string json)
    {
        var defaults = new AppSettings();
        json = JsonSerializer.Serialize(defaults, JsonOptions);
        Debug.WriteLine("[Settings] Default settings created.");
        return defaults;
    }


    // Keeps an unreadable file for the user before it is overwritten by defaults
    private void BackupBrokenFile()
    {
        try
        {
            File.Copy(_filePath, _filePath + ".bak", overwrite: true);
            Debug.WriteLine("[Settings] Unreadable settings file saved as .bak.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Failed to back up the settings file: {ex.Message}");
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Saving

    private void AtomicWrite(string json)
    {
        string tempPath = _filePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _filePath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }


    private async Task AtomicWriteAsync(string json, CancellationToken cancellationToken)
    {
        string tempPath = _filePath + ".tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);

            // File.Replace has no async counterpart; it is fast enough for a small file
            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _filePath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }


    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Failed to clean up temp file: {ex.Message}");
        }
    }


    private static void LogLoadFailure(Exception ex) =>
        Debug.WriteLine($"[Settings] Failed to load settings: {ex}");

    private static void LogSaveFailure(Exception ex) =>
        Debug.WriteLine($"[Settings] Failed to save settings: {ex}");

}

// END-OF-FILE
