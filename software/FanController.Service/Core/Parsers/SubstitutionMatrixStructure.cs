////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SubstitutionMatrixStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

/*
//
// Types and flags:
//

//#define FAN_SUBSTITUTION_MATRIX_SIZE    (FANS_COUNT * (FANS_COUNT - 1) / 2)		// 1 byte per 2 coefficients


//
// Prototype declaration:
//

// Fan Substitution Matrix
typedef struct __attribute__((aligned(4)))
{
	uint8_t data[FAN_SUBSTITUTION_MATRIX_SIZE];
} Fan_Substitution_Matrix;

*/


/// <summary>
/// The structure of the fans substitution matrix
/// </summary>
public ref struct SubstitutionMatrixStructure
{
    /// <summary>
    /// Matrix size: n·(n−1) coefficients (each fan by each of the others), 4 bits each
    /// </summary>
    public static int SizeForFanCount(int fanCount) => fanCount * (fanCount - 1) / 2;

    /// <summary>
    /// Fan Substitution Matrix size
    /// </summary>
    public readonly int Size;

    // Packed structure data
    private Span<byte> _dataSpan;


    /// <summary>
    /// Wraps the raw structure data for <paramref name="fanCount"/> fans.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public SubstitutionMatrixStructure(Span<byte> data, int fanCount)
    {
        Size = SizeForFanCount(fanCount);
        PackedStructureMethods.EnsureSize(data, Size, nameof(SubstitutionMatrixStructure));
        _dataSpan = data;
    }


    /// <summary>
    /// Fans substitution coefficients matrix array
    /// </summary>
    public Span<byte> FanSubstitutionMatrix
    {
        get => _dataSpan[..Size];
        set => value.CopyTo(_dataSpan[..Size]);
    }

}

// END-OF-FILE
