////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// TextBoxRestrictionBehavior.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;


namespace FanController.Behaviors;

/// <summary>
/// On-the-fly restriction of the TextBox input: only the characters of
/// <see cref="AllowedPattern"/> and at most <see cref="MaxLength"/> characters.
/// Typed text is blocked before it is inserted; pasted, dropped and bound
/// (initial) text is corrected afterwards.
/// </summary>
public class TextBoxRestrictionBehavior : Behavior<TextBox>
{
    // Matches one disallowed character (null: all characters are allowed)
    private Regex? _disallowedCharacter;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Styled properties

    // Maximum text length (0 or less: unlimited)
    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<TextBoxRestrictionBehavior, int>(nameof(MaxLength), 0);

    // Allowed characters as a regex character class, e.g. "[0-9a-zA-Z]" (null: all)
    public static readonly StyledProperty<string?> AllowedPatternProperty =
        AvaloniaProperty.Register<TextBoxRestrictionBehavior, string?>(nameof(AllowedPattern));

    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public string? AllowedPattern
    {
        get => GetValue(AllowedPatternProperty);
        set => SetValue(AllowedPatternProperty, value);
    }


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AllowedPatternProperty)
            _disallowedCharacter = BuildDisallowedCharacterRegex(AllowedPattern);
    }


    // "[abc]" -> a regex matching any character except a, b, c
    private static Regex? BuildDisallowedCharacterRegex(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;

        if ((pattern.Length < 3) || (pattern[0] != '[') || (pattern[^1] != ']'))
        {
            Debug.WriteLine($"[TextBoxRestrictionBehavior] Not a character class: '{pattern}'.");
            return null;
        }

        try
        {
            return new Regex($"[^{pattern[1..^1]}]", RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine($"[TextBoxRestrictionBehavior] Invalid pattern '{pattern}': {ex.Message}");
            return null;
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Attach / detach

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;

        // Keyboard input: handled before the text is inserted
        AssociatedObject.AddHandler(
            InputElement.TextInputEvent,
            OnTextInput,
            RoutingStrategies.Tunnel);

        // Any other change (paste, drag-drop, programmatic): corrected afterwards
        AssociatedObject.TextChanging += OnTextChanging;

        // Initial value set by a binding
        AssociatedObject.Loaded += OnLoaded;
    }


    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject is null) return;

        AssociatedObject.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        AssociatedObject.TextChanging -= OnTextChanging;
        AssociatedObject.Loaded -= OnLoaded;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Handlers

    private void OnLoaded(object? sender, RoutedEventArgs e) => CorrectText();


    // Blocks typed text with disallowed characters or exceeding MaxLength
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if ((AssociatedObject is not TextBox textBox) || string.IsNullOrEmpty(e.Text)) return;

        if ((_disallowedCharacter is not null) && _disallowedCharacter.IsMatch(e.Text))
        {
            e.Handled = true;
            return;
        }

        if (MaxLength > 0)
        {
            int selectionLength = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
            int resultLength = (textBox.Text?.Length ?? 0) - selectionLength + e.Text.Length;

            if (resultLength > MaxLength)
                e.Handled = true;
        }
    }


    // Fires on every text change (typed text is already valid here).
    // The text can't be changed inside TextChanging, so the correction is posted.
    private void OnTextChanging(object? sender, TextChangingEventArgs e)
    {
        if (AssociatedObject is not TextBox textBox) return;

        string text = textBox.Text ?? string.Empty;
        if (Sanitize(text) != text)
            Dispatcher.UIThread.Post(CorrectText);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Correction

    // Corrects the current text; the caret stays behind the same valid characters
    private void CorrectText()
    {
        if (AssociatedObject is not TextBox textBox) return;

        string text = textBox.Text ?? string.Empty;
        string sanitized = Sanitize(text);
        if (sanitized == text) return;

        int caretIndex = Math.Clamp(textBox.CaretIndex, 0, text.Length);
        int caret = Math.Min(Sanitize(text[..caretIndex]).Length, sanitized.Length);

        textBox.Text = sanitized;
        textBox.CaretIndex = caret;
    }


    // Applies the character filter and the length limit
    private string Sanitize(string text)
    {
        if (_disallowedCharacter is not null)
            text = _disallowedCharacter.Replace(text, string.Empty);

        if ((MaxLength > 0) && (text.Length > MaxLength))
            text = text[..MaxLength];

        return text;
    }
}

// END-OF-FILE
