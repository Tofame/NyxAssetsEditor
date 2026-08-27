using System;
using System.IO;

namespace NyxAssetsEditor.Services.ImportExport;

/// <summary>
/// Minimal pure-C# animated GIF89a encoder.
/// Supports transparency, per-frame delays, and infinite looping via the Netscape 2.0 extension.
/// Up to 255 unique opaque colours per frame; transparent pixels use palette slot 0.
/// </summary>
public static class GifEncoder
{
	// ── public API ────────────────────────────────────────────────────────────

	/// <summary>
	/// Encodes a sequence of RGBA frames into an animated GIF stream.
	/// </summary>
	/// <param name="output">Destination stream (must be writable).</param>
	/// <param name="frames">Array of frames; all frames should share dimensions.</param>
	/// <param name="loops">0 = loop forever; positive = exact repeat count.</param>
	public static void Encode(Stream output, GifFrame[] frames, int loops = 0)
	{
		if (frames == null || frames.Length == 0)
			return;

		int w = frames[0].Width;
		int h = frames[0].Height;

		WriteHeader(output, w, h);
		WriteNetscapeLoopBlock(output, loops);

		foreach (var frame in frames)
			WriteFrame(output, frame.Pixels, frame.Width, frame.Height, frame.DelayCs);

		output.WriteByte(0x3B); // GIF trailer
	}

	// ── frame struct ──────────────────────────────────────────────────────────

	public readonly struct GifFrame
	{
		public readonly byte[] Pixels;   // RGBA, row-major
		public readonly int Width;
		public readonly int Height;
		public readonly int DelayCs;     // centiseconds (1/100 s)

		public GifFrame(byte[] pixels, int width, int height, int delayCs)
		{
			Pixels = pixels;
			Width = width;
			Height = height;
			DelayCs = Math.Max(1, delayCs);
		}
	}

	// ── header ────────────────────────────────────────────────────────────────

	private static void WriteHeader(Stream s, int w, int h)
	{
		s.Write("GIF89a"u8);
		WriteWord(s, (ushort)w);
		WriteWord(s, (ushort)h);
		s.WriteByte(0x00); // packed: no global colour table
		s.WriteByte(0x00); // background colour index
		s.WriteByte(0x00); // pixel aspect ratio
	}

	// ── Netscape 2.0 loop extension ───────────────────────────────────────────

	private static void WriteNetscapeLoopBlock(Stream s, int loops)
	{
		s.WriteByte(0x21);
		s.WriteByte(0xFF);
		s.WriteByte(0x0B);
		s.Write("NETSCAPE2.0"u8);
		s.WriteByte(0x03);
		s.WriteByte(0x01);
		WriteWord(s, (ushort)(loops & 0xFFFF));
		s.WriteByte(0x00);
	}

	// ── single frame ─────────────────────────────────────────────────────────

	private static void WriteFrame(Stream s, byte[] rgba, int w, int h, int delayCs)
	{
		Quantize(rgba, w, h, out var palette, out var indices, out var transparentIndex);

		int paletteSlots = palette.Length / 3;
		int palBits = PaletteBits(paletteSlots);

		WriteGce(s, delayCs, transparentIndex);

		// Image Descriptor
		s.WriteByte(0x2C);
		WriteWord(s, 0);
		WriteWord(s, 0);
		WriteWord(s, (ushort)w);
		WriteWord(s, (ushort)h);
		s.WriteByte((byte)(0x80 | (palBits - 1))); // local colour table present

		s.Write(palette);

		LzwEncode(s, indices, palBits);
	}

	private static void WriteGce(Stream s, int delayCs, int transparentIndex)
	{
		bool hasTransp = transparentIndex >= 0;
		s.WriteByte(0x21);
		s.WriteByte(0xF9);
		s.WriteByte(0x04);
		s.WriteByte((byte)(0x08 | (hasTransp ? 0x01 : 0x00)));
		WriteWord(s, (ushort)delayCs);
		s.WriteByte(hasTransp ? (byte)transparentIndex : (byte)0);
		s.WriteByte(0x00);
	}

	// ── colour quantization ───────────────────────────────────────────────────

	private static void Quantize(byte[] rgba, int w, int h,
		out byte[] palette, out byte[] indices, out int transparentIndex)
	{
		int pixelCount = w * h;
		const int MaxOpaqueColors = 255;

		// Detect transparency first.
		bool hasTransparency = false;
		for (int i = 0; i < pixelCount; i++)
		{
			if (rgba[i * 4 + 3] < 128) { hasTransparency = true; break; }
		}

		// Try increasingly aggressive channel-precision reduction until ≤255 unique colours.
		// Masks: 0xFF (8-bit), 0xFE (7-bit), 0xFC (6-bit), 0xF8 (5-bit), 0xF0 (4-bit) …
		int mask = 0xFF;
		System.Collections.Generic.Dictionary<int, int> colorCounts;
		do
		{
			colorCounts = CountColors(rgba, pixelCount, mask);
			if (colorCounts.Count <= MaxOpaqueColors)
				break;
			mask = (mask << 1) & 0xFF; // reduce 1 bit of precision
		}
		while (mask != 0);

		if (mask == 0)
			mask = 0xF0; // at most 4096 combinations; bucket them hard

		// Re-count with final mask (in case we exited due to mask==0).
		if (colorCounts.Count > MaxOpaqueColors)
			colorCounts = CountColors(rgba, pixelCount, mask);

		// Build palette: slot 0 reserved for transparency when needed.
		int baseIdx = hasTransparency ? 1 : 0;
		var colorMap = new System.Collections.Generic.Dictionary<int, byte>(colorCounts.Count);
		{
			byte idx = (byte)baseIdx;
			foreach (var k in colorCounts.Keys)
				colorMap[k] = idx++;
		}

		int totalSlots = NextPow2(colorMap.Count + baseIdx);
		if (totalSlots < 2) totalSlots = 2;

		palette = new byte[totalSlots * 3];
		if (hasTransparency)
		{
			palette[0] = 0xFF;
			palette[1] = 0x00;
			palette[2] = 0xFF;
		}
		foreach (var (key, idx) in colorMap)
		{
			int po = idx * 3;
			palette[po]     = (byte)((key >> 16) & 0xFF);
			palette[po + 1] = (byte)((key >> 8) & 0xFF);
			palette[po + 2] = (byte)(key & 0xFF);
		}

		indices = new byte[pixelCount];
		for (int i = 0; i < pixelCount; i++)
		{
			int o = i * 4;
			if (rgba[o + 3] < 128) { indices[i] = 0; continue; }
			// Quantize the pixel with the same mask used to build the palette.
			int key = ((rgba[o] & mask) << 16) | ((rgba[o + 1] & mask) << 8) | (rgba[o + 2] & mask);
			indices[i] = colorMap.TryGetValue(key, out var ci) ? ci : (byte)baseIdx;
		}

		transparentIndex = hasTransparency ? 0 : -1;
	}

	private static System.Collections.Generic.Dictionary<int, int> CountColors(byte[] rgba, int pixelCount, int mask)
	{
		var counts = new System.Collections.Generic.Dictionary<int, int>(512);
		for (int i = 0; i < pixelCount; i++)
		{
			int o = i * 4;
			if (rgba[o + 3] < 128) continue;
			int key = ((rgba[o] & mask) << 16) | ((rgba[o + 1] & mask) << 8) | (rgba[o + 2] & mask);
			counts.TryGetValue(key, out var c);
			counts[key] = c + 1;
		}
		return counts;
	}


	// ── LZW encoding ──────────────────────────────────────────────────────────

	private static void LzwEncode(Stream s, byte[] indices, int palBits)
	{
		int minCodeSize = Math.Max(2, palBits);
		s.WriteByte((byte)minCodeSize);

		int clearCode = 1 << minCodeSize;
		int eofCode = clearCode + 1;

		var buf = new BitBuffer(s);

		int codeSize = minCodeSize + 1;
		int maxCode = 1 << codeSize;
		int nextCode = eofCode + 1;
		var table = new System.Collections.Generic.Dictionary<int, int>(4096);

		buf.Write(clearCode, codeSize);

		if (indices.Length == 0)
		{
			buf.Write(eofCode, codeSize);
			buf.Flush();
			s.WriteByte(0x00);
			return;
		}

		int prefix = indices[0];
		for (int i = 1; i < indices.Length; i++)
		{
			int suffix = indices[i];
			int tableKey = (prefix << 8) | suffix;

			if (table.TryGetValue(tableKey, out int existing))
			{
				prefix = existing;
			}
			else
			{
				buf.Write(prefix, codeSize);

				if (nextCode < 4096)
				{
					table[tableKey] = nextCode++;
					if (nextCode > maxCode && codeSize < 12)
					{
						codeSize++;
						maxCode <<= 1;
					}
				}
				else
				{
					buf.Write(clearCode, codeSize);
					table.Clear();
					codeSize = minCodeSize + 1;
					maxCode = 1 << codeSize;
					nextCode = eofCode + 1;
				}

				prefix = suffix;
			}
		}

		buf.Write(prefix, codeSize);
		buf.Write(eofCode, codeSize);
		buf.Flush();
		s.WriteByte(0x00);
	}

	// ── helpers ───────────────────────────────────────────────────────────────

	private static void WriteWord(Stream s, ushort v)
	{
		s.WriteByte((byte)(v & 0xFF));
		s.WriteByte((byte)(v >> 8));
	}

	private static int PaletteBits(int count)
	{
		for (int b = 1; b <= 8; b++)
			if (1 << b >= count) return b;
		return 8;
	}

	private static int NextPow2(int n)
	{
		if (n <= 2) return 2;
		int p = 1;
		while (p < n) p <<= 1;
		return p;
	}

	// ── bit-packing sub-block writer ──────────────────────────────────────────

	private sealed class BitBuffer
	{
		private readonly Stream _stream;
		private uint _bits;
		private int _bitCount;
		private readonly byte[] _block = new byte[256];
		private int _blockLen;

		public BitBuffer(Stream stream) { _stream = stream; }

		public void Write(int code, int codeSize)
		{
			_bits |= (uint)code << _bitCount;
			_bitCount += codeSize;
			while (_bitCount >= 8)
			{
				_block[_blockLen++] = (byte)(_bits & 0xFF);
				_bits >>= 8;
				_bitCount -= 8;
				if (_blockLen == 255) FlushBlock();
			}
		}

		private void FlushBlock()
		{
			_stream.WriteByte((byte)_blockLen);
			_stream.Write(_block, 0, _blockLen);
			_blockLen = 0;
		}

		public void Flush()
		{
			while (_bitCount > 0)
			{
				_block[_blockLen++] = (byte)(_bits & 0xFF);
				_bits >>= 8;
				_bitCount -= 8;
				if (_blockLen == 255) FlushBlock();
			}
			if (_blockLen > 0) FlushBlock();
		}
	}
}
