////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// PackedStructureMethods.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Buffers.Binary;
using System.Text;


namespace FanController.Service.Core.Parsers;

/// <summary>
/// Helpers for packed binary structures: size checks, little-endian (or big-endian)
/// integers and null-terminated ASCII strings at fixed offsets.
/// </summary>
public static class PackedStructureMethods
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Size check

    /// <summary>
    /// Throws if <paramref name="data"/> is shorter than the structure size.
    /// </summary>
    public static void EnsureSize(ReadOnlySpan<byte> data, int size, string structureName)
    {
        if (data.Length < size)
            throw new ArgumentException(
                $"Not enough data for {structureName}: {data.Length} of {size} bytes.",
                nameof(data));
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Integers

    public static UInt32 ReadUInt32FromSpan(ReadOnlySpan<byte> buffer, int index, bool bigEndian = false) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(buffer[index..])
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer[index..]);


    public static void WriteUInt32IntoSpan(UInt32 value, Span<byte> buffer, int index, bool bigEndian = false)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(buffer[index..], value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[index..], value);
    }


    public static UInt16 ReadUInt16FromSpan(ReadOnlySpan<byte> buffer, int index, bool bigEndian = false) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(buffer[index..])
            : BinaryPrimitives.ReadUInt16LittleEndian(buffer[index..]);


    public static void WriteUInt16IntoSpan(UInt16 value, Span<byte> buffer, int index, bool bigEndian = false)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt16BigEndian(buffer[index..], value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[index..], value);
    }


    public static Int16 ReadInt16FromSpan(ReadOnlySpan<byte> buffer, int index, bool bigEndian = false) =>
        bigEndian
            ? BinaryPrimitives.ReadInt16BigEndian(buffer[index..])
            : BinaryPrimitives.ReadInt16LittleEndian(buffer[index..]);


    public static void WriteInt16IntoSpan(Int16 value, Span<byte> buffer, int index, bool bigEndian = false)
    {
        if (bigEndian)
            BinaryPrimitives.WriteInt16BigEndian(buffer[index..], value);
        else
            BinaryPrimitives.WriteInt16LittleEndian(buffer[index..], value);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Strings

    /// <summary>
    /// Reads a null-terminated ASCII string (the text up to the first '\0').
    /// </summary>
    public static string ReadAsciiString(ReadOnlySpan<byte> buffer)
    {
        int length = buffer.IndexOf((byte)0);
        return Encoding.ASCII.GetString((length >= 0) ? buffer[..length] : buffer);
    }


    /// <summary>
    /// Writes a null-terminated ASCII string, truncated to fit the field
    /// (one byte is reserved for the terminator); the rest of the field is zero-filled.
    /// Non-ASCII characters are replaced with '?'.
    /// </summary>
    public static void WriteAsciiString(string value, Span<byte> buffer)
    {
        buffer.Clear();
        int length = Math.Min(value.Length, buffer.Length - 1);
        if (length > 0)
            Encoding.ASCII.GetBytes(value.AsSpan(0, length), buffer);
    }
}

// END-OF-FILE
