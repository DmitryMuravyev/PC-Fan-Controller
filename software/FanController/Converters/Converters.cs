////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// Converters.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using FanController.Contracts;
using FanController.Localization;
using static FanController.ViewModels.FanItem;
using static FanController.ViewModels.HostItem;
using static FanController.ViewModels.MainWindowViewModel;
using static FanController.ViewModels.SensorItem;


namespace FanController.Converters;

////////////////////////////////////////////////////////////////////////////////////////
// Helpers

/// <summary>
/// Shared helpers of the converters
/// </summary>
internal static class ConverterHelpers
{
    // A converter parameter meaning "true": the bool true or the string "True"
    public static bool IsTrue(object? parameter) =>
        (parameter is true) ||
        ((parameter is string s) && bool.TryParse(s, out bool parsed) && parsed);


    // Status brush from the theme (ThemeResources.axaml):
    // "Status<State>" (solid) or "OverallState<State>" (semi-transparent)
    public static IBrush? StatusBrush(string state, bool solid) =>
        Application.Current?.FindResource((solid ? "Status" : "OverallState") + state) as IBrush;
}


/// <summary>
/// Bitmaps from the application's Assets folder (AvaloniaResource),
/// each loaded once on first use
/// </summary>
internal static class AssetBitmaps
{
    private static readonly string AssemblyName = typeof(App).Assembly.GetName().Name!;
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    public static Bitmap Get(string file) => Cache.GetOrAdd(file, Load);

    private static Bitmap Load(string file)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://{AssemblyName}/Assets/{file}"));
        return new Bitmap(stream);
    }
}


////////////////////////////////////////////////////////////////////////////////////////
// Logic and general text

/// <summary>
/// [0] IsContentEnabled, [1] IsBusy: the content is enabled and not busy
/// </summary>
public sealed class ContentEnabledConverter : IMultiValueConverter
{
    public static readonly ContentEnabledConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        (values.Count >= 2) && (values[0] is true) && (values[1] is false);
}


/// <summary>
/// [0] and [1]: both values are true (AND)
/// </summary>
public sealed class BooleanStateConverter : IMultiValueConverter
{
    public static readonly BooleanStateConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        (values.Count >= 2) && (values[0] is true) && (values[1] is true);
}


/// <summary>
/// Returns true when the bound enum value equals the ConverterParameter
/// (NotInstance: when it doesn't). Eliminates the need for IsControlPwm, IsProfileSilent, etc.
/// </summary>
public sealed class EnumEqualConverter : IValueConverter
{
    public static readonly EnumEqualConverter Instance = new();

    public static readonly IValueConverter NotInstance = new FuncValueConverter<object?, object?, bool>(
        (value, parameter) =>
        {
            var result = Instance.Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture);
            return result is bool boolResult && !boolResult;
        });

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((value is null) || (parameter is null)) return false;
        return value.Equals(parameter);
    }

    // For a two-way RadioButton binding, if ever needed:
    /*
    public object? ConvertBack(object? value, Type targetType,
                               object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
            return parameter;
        return Avalonia.Data.BindingOperations.DoNothing;
    }
    */

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// [0] text, [1] placeholder: the placeholder if the text is empty
/// </summary>
public sealed class EmptyToPlaceholderConverter : IMultiValueConverter
{
    public static readonly EmptyToPlaceholderConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = (values.Count > 0) ? values[0] as string : null;
        string? placeholder = (values.Count > 1) ? values[1] as string : null;

        return string.IsNullOrWhiteSpace(text) ? placeholder : text;
    }
}


/// <summary>
/// [0] bool, [1] text if true, [2] text if false
/// </summary>
public sealed class BoolToStringConverter : IMultiValueConverter
{
    public static readonly BoolToStringConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((values.Count < 3) || (values[0] is not bool b)) return string.Empty;
        return b ? values[1]?.ToString() : values[2]?.ToString();
    }
}


/// <summary>
/// [0] value, [1] composite format string (optional): the formatted value; "-" if the value is null
/// </summary>
public sealed class StringFormatConverter : IMultiValueConverter
{
    public static readonly StringFormatConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1) return string.Empty;
        if (values[0] is null) return "-";

        if ((values.Count < 2) || (values[1] is not string format))
            return values[0]!.ToString();

        try { return string.Format(format, values[0]); }
        catch (FormatException) { return values[0]?.ToString(); }
    }
}


/// <summary>
/// Validation errors as one line per error
/// </summary>
public sealed class ValidationErrorsConverter : IValueConverter
{
    public static readonly ValidationErrorsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<ValidationResult> errors)
            return string.Join(Environment.NewLine, errors.Select(e => e.ErrorMessage));

        if (value is IEnumerable<object> objErrors)
            return string.Join(Environment.NewLine, objErrors);

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


////////////////////////////////////////////////////////////////////////////////////////
// Localized text
// Extra (ignored) bindings may be added, e.g. to LocalizationService.Instance.CurrentCulture,
// so that the text is re-evaluated when the language is changed at runtime.

/// <summary>
/// [0] enum value, [1] resource type (default "Value"), [2] format type (optional):
/// the localized enum text from the key "{EnumType}{Value}{ResourceType}",
/// formatted with "{EnumType}{FormatType}" if given (e.g. "FanModelSizeSizeSmallValue").
/// </summary>
public sealed class EnumToLocalizedTextConverter : IMultiValueConverter
{
    public static readonly EnumToLocalizedTextConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((values.Count < 1) || (values[0] is not object value)) return string.Empty;

        string resourceType = ((values.Count > 1) && (values[1] is string t)) ? t : "Value";
        string? formatType = ((values.Count > 2) && (values[2] is string f)) ? f : null;

        string typeName = value.GetType().Name;
        string result = LocalizationService.Instance[$"{typeName}{value}{resourceType}"];

        if (formatType is not null)
        {
            try { result = string.Format(LocalizationService.Instance[typeName + formatType], result); }
            catch (FormatException) { /* the unformatted text is shown */ }
        }

        return result;
    }
}


/// <summary>
/// [0] connected: the main window title with the connection state
/// </summary>
public sealed class StatusTitleConverter : IMultiValueConverter
{
    public static readonly StatusTitleConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isConnected = (values.Count > 0) && (values[0] is true);

        return string.Concat(
            LocalizationService.Instance["ApplicationTitle"],
            " - ",
            LocalizationService.Instance[isConnected ? "ControllerConnectedCaption" : "ControllerDisconnectedCaption"]);
    }
}


////////////////////////////////////////////////////////////////////////////////////////
// Numeric values

/// <summary>
/// 0..1 to "NN %"
/// </summary>
public sealed class DoubleToPercentConverter : IValueConverter
{
    public static readonly DoubleToPercentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is double percentage) ? $"{(percentage * 100.0):F0} %" : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// [0] 0..1, [1] enabled: "NN %", or "-" when not enabled
/// </summary>
public sealed class OptionalDoubleToPercentConverter : IMultiValueConverter
{
    public static readonly OptionalDoubleToPercentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((values.Count < 1) || (values[0] is not double value)) return null;

        bool isEnabled = (values.Count > 1) && (values[1] is true);
        return isEnabled
            ? DoubleToPercentConverter.Instance.Convert(value, targetType, null, culture)
            : "-";
    }
}


/// <summary>
/// [0] bytes, [1] divider (string, e.g. "1024"), [2] format (optional)
/// </summary>
public sealed class BytesConverter : IMultiValueConverter
{
    public static readonly BytesConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        int bytes = ((values.Count > 0) && (values[0] is int b)) ? b : 0;
        int divider = ((values.Count > 1) && (values[1] is string d) && int.TryParse(d, out int parsed) && (parsed > 0))
            ? parsed
            : 1;
        string? format = ((values.Count > 2) && (values[2] is string f)) ? f : null;

        double result = (double)bytes / divider;
        if (format is null) return result;

        try { return string.Format(format, result); }
        catch (FormatException) { return result; }
    }
}


/// <summary>
/// [0] pulses per minute, [1] pulses per revolution, [2] enabled, [3] format (optional):
/// the fan RPM, or "-" when not enabled
/// </summary>
public sealed class PpmToRpmConverter : IMultiValueConverter
{
    public static readonly PpmToRpmConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        uint ppm = ((values.Count > 0) && (values[0] is uint p)) ? p : 0;
        int ppr = ((values.Count > 1) && (values[1] is int r) && (r > 0)) ? r : 2;
        bool isEnabled = (values.Count <= 2) || (values[2] is not false);
        string? format = ((values.Count > 3) && (values[3] is string f)) ? f : null;

        object result = isEnabled ? (int)Math.Round((double)ppm / ppr) : "-";
        if (format is null) return result;

        try { return string.Format(format, result); }
        catch (FormatException) { return result; }
    }
}


/// <summary>
/// Control basis (0..1, 0.5 = full range) to the resulting control range:
/// "NN-NN %" without a parameter, the left bound (parameter true) or the right bound (false).
/// </summary>
public sealed class BasisToRangeConverter : IValueConverter
{
    public static readonly BasisToRangeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double basis = (value is double b) ? b : 0.5;

        double leftBound = (basis > 0.5) ? ((basis - 0.5) * 2.0) : 0.0;
        double rightBound = (basis > 0.5) ? 1.0 : (1.0 - ((0.5 - basis) * 2.0));

        if (parameter is null)
            return string.Format("{0:F0}-{1:F0} %", leftBound * 100.0, rightBound * 100.0);

        return ConverterHelpers.IsTrue(parameter) ? leftBound : rightBound;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


////////////////////////////////////////////////////////////////////////////////////////
// Bars (0..1 fractions and widths)

/// <summary>
/// [0] pulses per minute, [1] minimum, [2] maximum: the position within the range (0..1;
/// 0 while the range is unknown or empty)
/// </summary>
public sealed class PpmToPercentConverter : IMultiValueConverter
{
    public static readonly PpmToPercentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        uint ppm = ((values.Count > 0) && (values[0] is uint p)) ? p : 0;
        uint minPpm = ((values.Count > 1) && (values[1] is uint min)) ? min : 0;
        uint maxPpm = ((values.Count > 2) && (values[2] is uint max)) ? max : 0;

        if (maxPpm <= minPpm) return 0.0;

        ppm = Math.Clamp(ppm, minPpm, maxPpm);
        return (double)(ppm - minPpm) / (maxPpm - minPpm);
    }
}


/// <summary>
/// [0] temperature: the position on the 0..100 °C bar (0..1)
/// </summary>
public sealed class TemperatureToPercentConverter : IMultiValueConverter
{
    public static readonly TemperatureToPercentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double temperature = ((values.Count > 0) && (values[0] is double t)) ? t : 0.0;
        return Math.Clamp(temperature / 100.0, 0.0, 1.0);
    }
}


/// <summary>
/// [0] fraction (0..1), [1] container width: the bar width
/// </summary>
public sealed class PercentBarWidthConverter : IMultiValueConverter
{
    public static readonly PercentBarWidthConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double fraction = ((values.Count > 0) && (values[0] is double p) && !double.IsNaN(p)) ? p : 0.0;
        double totalWidth = ((values.Count > 1) && (values[1] is double w)) ? w : 100.0;

        return Math.Clamp(totalWidth * fraction, 0.0, totalWidth);
    }
}


////////////////////////////////////////////////////////////////////////////////////////
// Colors and brushes

/// <summary>
/// [0] connected: the semi-transparent normal / error brush of the theme
/// </summary>
public sealed class DeviceConnectedColorConverter : IMultiValueConverter
{
    public static readonly DeviceConnectedColorConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isConnected = (values.Count > 0) && (values[0] is true);
        return ConverterHelpers.StatusBrush(isConnected ? "Normal" : "Error", solid: false);
    }
}


/// <summary>
/// Fan status to the theme brush (parameter true: solid, otherwise semi-transparent)
/// </summary>
public sealed class FanStatusToBackgroundConverter : IValueConverter
{
    public static readonly FanStatusToBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string state = value switch
        {
            FanStatus.StatusRunning => "Normal",
            FanStatus.StatusHalted => "Suspended",
            FanStatus.StatusPreWarming or FanStatus.StatusWarming => "Busy",
            FanStatus.StatusScanning => "Action",
            FanStatus.StatusWarning => "Warning",
            FanStatus.StatusError or FanStatus.StatusFailed => "Error",
            _ => "Unavailable"      // Disabled or unknown
        };

        return ConverterHelpers.StatusBrush(state, ConverterHelpers.IsTrue(parameter));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Sensor status to the theme brush (parameter true: solid, otherwise semi-transparent)
/// </summary>
public sealed class SensorStatusToBackgroundConverter : IValueConverter
{
    public static readonly SensorStatusToBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string state = value switch
        {
            SensorStatus.StatusValid => "Normal",
            SensorStatus.StatusOutdated => "Suspended",
            SensorStatus.StatusWarning => "Warning",
            SensorStatus.StatusAlarm or SensorStatus.StatusError => "Error",
            _ => "Unavailable"      // Disabled or unknown
        };

        return ConverterHelpers.StatusBrush(state, ConverterHelpers.IsTrue(parameter));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Host status to the theme brush (parameter true: solid, otherwise semi-transparent)
/// </summary>
public sealed class HostStatusToBackgroundConverter : IValueConverter
{
    public static readonly HostStatusToBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string state = value switch
        {
            HostStatus.StatusOnline => "Normal",
            HostStatus.StatusIncompatible => "Error",
            _ => "Unavailable"      // Offline or unknown
        };

        return ConverterHelpers.StatusBrush(state, ConverterHelpers.IsTrue(parameter));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// 0..1 to a bar color: blue at 0, magenta at 1
/// </summary>
public sealed class PercentToColorConverter : IValueConverter
{
    public static readonly PercentToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double fraction = (value is double p) ? p : 0.0;
        byte intensity = (byte)Math.Clamp(fraction * 255.0, 0.0, 255.0);

        return new ImmutableSolidColorBrush(Color.FromRgb(intensity, 0x40, 0xFF));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// [0] temperature, [1] warning threshold, [2] alarm threshold: the temperature color.
/// Overcool below (2 * warning - alarm), then gradients overcool → warning → error,
/// error from the alarm threshold. Negative temperatures use the semi-transparent brushes.
/// </summary>
public sealed class TemperatureToColorConverter : IMultiValueConverter
{
    public static readonly TemperatureToColorConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double temperature = ((values.Count > 0) && (values[0] is double t)) ? t : 0.0;
        double warningTemperature = ((values.Count > 1) && (values[1] is double w)) ? w : 60.0;
        double alarmTemperature = ((values.Count > 2) && (values[2] is double a)) ? a : 70.0;

        if (warningTemperature > alarmTemperature) warningTemperature = alarmTemperature;
        double normalTemperature = (2.0 * warningTemperature) - alarmTemperature;

        bool solid = temperature >= 0.0;

        // Overcool range
        if (temperature < normalTemperature)
            return ConverterHelpers.StatusBrush("Overcool", solid);

        // Alarm range
        if (temperature >= alarmTemperature)
            return ConverterHelpers.StatusBrush("Error", solid);

        // Gradient ranges: overcool → warning, warning → error
        bool belowWarning = temperature < warningTemperature;
        var start = ConverterHelpers.StatusBrush(belowWarning ? "Overcool" : "Warning", solid) as ISolidColorBrush;
        var end = ConverterHelpers.StatusBrush(belowWarning ? "Warning" : "Error", solid) as ISolidColorBrush;
        double position = belowWarning
            ? (temperature - normalTemperature) / (warningTemperature - normalTemperature)
            : (temperature - warningTemperature) / (alarmTemperature - warningTemperature);

        if ((start is null) || (end is null))
            return ConverterHelpers.StatusBrush("Unavailable", solid);

        return new ImmutableSolidColorBrush(Lerp(start.Color, end.Color, position), start.Opacity);
    }


    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)Math.Round(from.A + ((to.A - from.A) * t)),
            (byte)Math.Round(from.R + ((to.R - from.R) * t)),
            (byte)Math.Round(from.G + ((to.G - from.G) * t)),
            (byte)Math.Round(from.B + ((to.B - from.B) * t)));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// The theme's ButtonBackground, tinted toward a fixed color when [0] is true.
/// Bindings: [0] bool, [1] the element itself ($self, to read ActualThemeVariant),
/// [2] $self.ActualThemeVariant (forces re-evaluation on a theme change).
/// </summary>
public sealed class TintedBackgroundConverter : IMultiValueConverter
{
    public static readonly TintedBackgroundConverter Instance = new();

    // Tint color and strength (0: none, 1: the tint color)
    private static readonly Color TintColor = Colors.CornflowerBlue;
    private const double TintStrength = 0.75;

    // Fallback: semi-transparent white (the Avalonia dark theme default)
    private static readonly IBrush FallbackBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return AvaloniaProperty.UnsetValue;

        ThemeVariant themeVariant = (values[1] is StyledElement element)
            ? element.ActualThemeVariant
            : ThemeVariant.Default;

        IBrush baseBrush = GetButtonBackgroundBrush(themeVariant);
        if (values[0] is not true) return baseBrush;

        return new ImmutableSolidColorBrush(Lerp(ExtractColor(baseBrush), TintColor, TintStrength));
    }


    private static IBrush GetButtonBackgroundBrush(ThemeVariant variant) =>
        ((Application.Current?.TryGetResource("ButtonBackground", variant, out object? resource) is true) &&
         (resource is IBrush brush))
            ? brush
            : FallbackBrush;


    // Gradient and other brushes: a neutral semi-transparent grey
    private static Color ExtractColor(IBrush brush) =>
        (brush is ISolidColorBrush solid) ? solid.Color : Color.FromArgb(0x33, 0x80, 0x80, 0x80);


    // Linear interpolation; the alpha of "from" is kept (theme transparency)
    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            from.A,
            (byte)(from.R + ((to.R - from.R) * t)),
            (byte)(from.G + ((to.G - from.G) * t)),
            (byte)(from.B + ((to.B - from.B) * t)));
    }
}


////////////////////////////////////////////////////////////////////////////////////////
// Icons and images

/// <summary>
/// [0] overall state: the window icon (32 px on Windows, 64 px elsewhere)
/// </summary>
public sealed class StatusIconConverter : IMultiValueConverter
{
    public static readonly StatusIconConverter Instance = new();

    private static readonly string IconSize = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "32" : "64";

    private static readonly WindowIcon StateNormalIcon = new(AssetBitmaps.Get($"Icon_{IconSize}_Normal.png"));
    private static readonly WindowIcon StateWarningIcon = new(AssetBitmaps.Get($"Icon_{IconSize}_Warning.png"));
    private static readonly WindowIcon StateAlarmIcon = new(AssetBitmaps.Get($"Icon_{IconSize}_Alarm.png"));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        OverallStates overallState = ((values.Count > 0) && (values[0] is OverallStates s)) ? s : OverallStates.StateError;

        return overallState switch
        {
            OverallStates.StateError => StateAlarmIcon,
            OverallStates.StateWarning => StateWarningIcon,
            _ => StateNormalIcon
        };
    }
}


/// <summary>
/// Fan control mode icon
/// </summary>
public sealed class FanControlModeToIconConverter : IValueConverter
{
    public static readonly FanControlModeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetBitmaps.Get(value switch
        {
            FanControlMode.ControlPWM => "Control_PWM.png",
            FanControlMode.ControlDCDC => "Control_DC_DC.png",
            FanControlMode.ControlONOFF => "Control_ON_OFF.png",
            _ => "Control_None.png"     // Disabled or unknown
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Fan operation mode icon
/// </summary>
public sealed class FanOperationModeToIconConverter : IValueConverter
{
    public static readonly FanOperationModeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetBitmaps.Get(value switch
        {
            FanOperationMode.OperationManual => "Operation_Manual.png",
            FanOperationMode.OperationSilent => "Operation_Silent.png",
            FanOperationMode.OperationTurbo => "Operation_Turbo.png",
            _ => "Operation_Temperature.png"     // Normal or unknown
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Sensor activity icon (active / inactive)
/// </summary>
public sealed class SensorActivityToIconConverter : IValueConverter
{
    public static readonly SensorActivityToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetBitmaps.Get((value is true) ? "Control_ON.png" : "Control_None.png");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Sensor type icon
/// </summary>
public sealed class SensorTypeToIconConverter : IValueConverter
{
    public static readonly SensorTypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetBitmaps.Get(value switch
        {
            TSensorType.MCU or TSensorType.DS1841 => "Sensor_Hardware.png",
            TSensorType.DS18B20 => "Sensor_DS18B20.png",
            TSensorType.SW => "Sensor_Software.png",
            _ => "Sensor_Unknown.png"       // Unknown, Other
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// Host hardware type icon
/// </summary>
public sealed class HardwareTypeIconConverter : IValueConverter
{
    public static readonly HardwareTypeIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetBitmaps.Get(value switch
        {
            HostHardwareType.CPU => "Device_CPU.png",
            HostHardwareType.Motherboard => "Device_Motherboard.png",
            HostHardwareType.GPU => "Device_GPU.png",
            HostHardwareType.Memory => "Device_Memory.png",
            HostHardwareType.HDD => "Device_HDD.png",
            HostHardwareType.Controller => "Device_Controller.png",
            HostHardwareType.Network => "Device_Network.png",
            HostHardwareType.PSU => "Device_PSU.png",
            HostHardwareType.Battery => "Device_Battery.png",
            HostHardwareType.Cooler => "Device_Cooler.png",
            HostHardwareType.Sensor => "Device_Sensor.png",
            _ => "Device_Unknown.png"
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// bool to a check mark (true) or a cross (false) drawing
/// </summary>
public sealed class BoolToCheckmarkConverter : IValueConverter
{
    public static readonly BoolToCheckmarkConverter Instance = new();

    private static readonly GeometryDrawing CheckDrawing = new()
    {
        Geometry = Geometry.Parse("M 2,8 L 6,12 L 14,4"),
        Pen = new Pen(Brushes.LimeGreen, 2, lineCap: PenLineCap.Round)
    };

    private static readonly GeometryDrawing CrossDrawing = new()
    {
        Geometry = Geometry.Parse("M 3,3 L 13,13 M 13,3 L 3,13"),
        Pen = new Pen(Brushes.Red, 2, lineCap: PenLineCap.Round)
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ? CheckDrawing : CrossDrawing;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


////////////////////////////////////////////////////////////////////////////////////////
// Not used at the moment (kept for future use)

/*
/// <summary>
/// Overall state to the semi-transparent theme brush
/// </summary>
public sealed class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string state = value switch
        {
            OverallStates.StateError => "Error",
            OverallStates.StateWarning => "Warning",
            OverallStates.StateNormal => "Normal",
            _ => "Unavailable"
        };

        return ConverterHelpers.StatusBrush(state, solid: false);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// DataGrid row number (the parameter is the DataGrid)
/// </summary>
public sealed class RowIndexConverter : IValueConverter
{
    public static readonly RowIndexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((value is null) || (parameter is not DataGrid grid)) return null;
        if (grid.ItemsSource is not System.Collections.IList items) return null;

        int index = items.IndexOf(value);
        return (index >= 0) ? (index + 1).ToString() : string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// [0] 0..1, [1] enhanced: "NN %", with "+" when enhanced
/// </summary>
public sealed class EnhancedDoubleToPercentConverter : IMultiValueConverter
{
    public static readonly EnhancedDoubleToPercentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((values.Count < 1) || (values[0] is not double value)) return null;

        bool enhanced = (values.Count > 1) && (values[1] is true);
        var result = DoubleToPercentConverter.Instance.Convert(value, targetType, null, culture);

        return (enhanced && (result is string text)) ? text + "+" : result;
    }
}


/// <summary>
/// bool inversion (both directions)
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool b) ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is bool b) ? !b : value;
}


/// <summary>
/// Voltage to "N.NN V" (negative: unknown)
/// </summary>
public sealed class DoubleToVoltageConverter : IValueConverter
{
    public static readonly DoubleToVoltageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ((value is double voltage) && (voltage >= 0.0)) ? $"{voltage:F2} V" : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


// For the Windows OS we can use TitleBar to display custom menu, overall state color and so on...
// Since this project is cross-platform, I will not use this feature, but I will save the code for the future.
public sealed class MaximizedPaddingConverter : IValueConverter
{
    public static readonly MaximizedPaddingConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is WindowState.Maximized)
            ? new Thickness(0, 8, 0, 0)     // 8 = Windows top border compensation
            : new Thickness(0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>
/// [0] bool, [1] brush (or color) if true, [2] brush (or color) if false.
/// Resources defined as colors (e.g. SystemBaseLowColor) arrive as Color, not IBrush.
/// </summary>
public sealed class BoolToBrushConverter : IMultiValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    private static readonly IBrush DefaultTrueBrush = Brushes.Green;
    private static readonly IBrush DefaultFalseBrush = Brushes.Gray;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool condition = (values.Count > 0) && (values[0] is true);
        object? source = condition
            ? ((values.Count > 1) ? values[1] : null)
            : ((values.Count > 2) ? values[2] : null);

        return source switch
        {
            IBrush brush => brush,
            Color color => new ImmutableSolidColorBrush(color),
            _ => condition ? DefaultTrueBrush : DefaultFalseBrush
        };
    }
}
*/

// END-OF-FILE
