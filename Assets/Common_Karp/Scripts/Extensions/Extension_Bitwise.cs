using UnityEngine;
using System;

public static class Extension_Bitwise
{
	public static int AsBitShift(this int value) => 1 << value;
	public static int SetBit(this int value, int bitPosition) => value | (1 << bitPosition);
	public static int ClearBit(this int value, int bitPosition) => value & ~(1 << bitPosition);
	public static int ToggleBit(this int value, int bitPosition) => value ^ (1 << bitPosition);
	public static bool HasBitSet(this int value, int bitPosition) => (value & (1 << bitPosition)) != 0;
	public static bool ContainBit(this int value, int bitPosition) => (value & (1 << bitPosition)) != 0;

	public static int CountSetBits(this int value)
	{
		int count = 0;
		uint u = (uint)value;
		while (u != 0)
		{
			u &= u - 1;
			count++;
		}
		return count;
	}

	public static bool ContainsLayer(this LayerMask layerMask, int layer) => ((int)layerMask).HasBitSet(layer);


	// ──────────────────────────────────────────────────────────────────────────────
	// ENUM FLAGS — Safe generic methods with type dispatch
	// ──────────────────────────────────────────────────────────────────────────────

	/// <summary>Check if enum has all flags set (bitwise AND). Works for byte/short/int/long enums.</summary>
	public static bool Has<T>(this T flags, T value) where T : Enum
	{
		return Enum.GetUnderlyingType(typeof(T)) switch
		{
			Type t when t == typeof(byte) => ((byte)(object)flags & (byte)(object)value) == (byte)(object)value,
			Type t when t == typeof(short) => ((short)(object)flags & (short)(object)value) == (short)(object)value,
			Type t when t == typeof(int) => ((int)(object)flags & (int)(object)value) == (int)(object)value,
			Type t when t == typeof(long) => ((long)(object)flags & (long)(object)value) == (long)(object)value,
			_ => throw new NotSupportedException($"Enum type {typeof(T).Name} not supported")
		};
	}
	/// <summary>Check if enum equals value exactly.</summary>
	public static bool Is<T>(this T flags, T value) where T : Enum => flags.Equals(value);
	/// <summary>Add flag to enum (bitwise OR).</summary>
	public static T Add<T>(this T flags, T value) where T : Enum
	{
		return Enum.GetUnderlyingType(typeof(T)) switch
		{
			Type t when t == typeof(byte) => (T)(object)((byte)(object)flags | (byte)(object)value),
			Type t when t == typeof(short) => (T)(object)((short)(object)flags | (short)(object)value),
			Type t when t == typeof(int) => (T)(object)((int)(object)flags | (int)(object)value),
			Type t when t == typeof(long) => (T)(object)((long)(object)flags | (long)(object)value),
			_ => throw new NotSupportedException($"Enum type {typeof(T).Name} not supported")
		};
	}

	/// <summary>Remove flag from enum (bitwise AND NOT).</summary>
	public static T Remove<T>(this T flags, T value) where T : Enum
	{
		return Enum.GetUnderlyingType(typeof(T)) switch
		{
			Type t when t == typeof(byte) => (T)(object)((byte)(object)flags & ~(byte)(object)value),
			Type t when t == typeof(short) => (T)(object)((short)(object)flags & ~(short)(object)value),
			Type t when t == typeof(int) => (T)(object)((int)(object)flags & ~(int)(object)value),
			Type t when t == typeof(long) => (T)(object)((long)(object)flags & ~(long)(object)value),
			_ => throw new NotSupportedException($"Enum type {typeof(T).Name} not supported")
		};
	}
	/// <summary>Toggle flag in enum (bitwise XOR).</summary>
	public static T Toggle<T>(this T flags, T value) where T : Enum
	{
		return Enum.GetUnderlyingType(typeof(T)) switch
		{
			Type t when t == typeof(byte) => (T)(object)((byte)(object)flags ^ (byte)(object)value),
			Type t when t == typeof(short) => (T)(object)((short)(object)flags ^ (short)(object)value),
			Type t when t == typeof(int) => (T)(object)((int)(object)flags ^ (int)(object)value),
			Type t when t == typeof(long) => (T)(object)((long)(object)flags ^ (long)(object)value),
			_ => throw new NotSupportedException($"Enum type {typeof(T).Name} not supported")
		};
	}
}