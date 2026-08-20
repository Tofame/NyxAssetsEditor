using System;

namespace NyxAssetsEditor.Services.Rendering;

public static class SpriteTransformUtil
{
	public static byte[] RotSprite(byte[] srcRgba, int w, int h, double angleDegrees)
	{
		// Bail on empty sprites
		bool hasContent = false;
		for (int i = 3; i < srcRgba.Length; i += 4)
		{
			if (srcRgba[i] > 0) { hasContent = true; break; }
		}
		if (!hasContent)
			return (byte[])srcRgba.Clone();

		// === Stage 1: 8x upscale via modified Scale2x applied 3 times (2^3 = 8) ===
		int curW = w;
		int curH = h;
		byte[] current = srcRgba;

		for (int pass = 0; pass < 3; pass++)
		{
			int newW = curW * 2;
			int newH = curH * 2;
			byte[] scaled = new byte[newW * newH * 4];

			for (int y = 0; y < curH; y++)
			{
				for (int x = 0; x < curW; x++)
				{
					int xm1 = Math.Max(x - 1, 0);
					int xp1 = Math.Min(x + 1, curW - 1);
					int ym1 = Math.Max(y - 1, 0);
					int yp1 = Math.Min(y + 1, curH - 1);

					int idxA = (ym1 * curW + xm1) * 4;
					int idxB = (ym1 * curW + x) * 4;
					int idxC = (ym1 * curW + xp1) * 4;
					int idxD = (y * curW + xm1) * 4;
					int idxE = (y * curW + x) * 4;
					int idxF = (y * curW + xp1) * 4;
					int idxG = (yp1 * curW + xm1) * 4;
					int idxH = (yp1 * curW + x) * 4;
					int idxI = (yp1 * curW + xp1) * 4;

					int ox = x * 2;
					int oy = y * 2;

					int p1Idx = (oy * newW + ox) * 4;
					int p2Idx = (oy * newW + ox + 1) * 4;
					int p3Idx = ((oy + 1) * newW + ox) * 4;
					int p4Idx = ((oy + 1) * newW + ox + 1) * 4;

					bool bSimD = PixelsSimilar(current, idxB, current, idxD);
					bool bSimF = PixelsSimilar(current, idxB, current, idxF);
					bool dSimH = PixelsSimilar(current, idxD, current, idxH);
					bool fSimH = PixelsSimilar(current, idxF, current, idxH);

					if (bSimD && !bSimF && !dSimH)
						CopyPixel(current, idxD, scaled, p1Idx);
					else
						CopyPixel(current, idxE, scaled, p1Idx);

					if (bSimF && !bSimD && !fSimH)
						CopyPixel(current, idxF, scaled, p2Idx);
					else
						CopyPixel(current, idxE, scaled, p2Idx);

					if (dSimH && !bSimD && !fSimH)
						CopyPixel(current, idxD, scaled, p3Idx);
					else
						CopyPixel(current, idxE, scaled, p3Idx);

					if (fSimH && !bSimF && !dSimH)
						CopyPixel(current, idxF, scaled, p4Idx);
					else
						CopyPixel(current, idxE, scaled, p4Idx);
				}
			}

			current = scaled;
			curW = newW;
			curH = newH;
		}

		int hiW = curW;
		int hiH = curH;
		byte[] hiRes = current;

		// === Stage 2: Padded NN Rotation at 8x resolution (double dimensions to avoid clipping during rotation) ===
		int padHiW = hiW * 2;
		int padHiH = hiH * 2;
		double srcCx = (hiW - 1) / 2.0;
		double srcCy = (hiH - 1) / 2.0;
		double dstCx = (padHiW - 1) / 2.0;
		double dstCy = (padHiH - 1) / 2.0;

		double rad = -angleDegrees * Math.PI / 180.0;
		double cosA = Math.Cos(rad);
		double sinA = Math.Sin(rad);

		byte[] rotatedPad8x = new byte[padHiW * padHiH * 4];

		for (int dy = 0; dy < padHiH; dy++)
		{
			double y0 = dy - dstCy;
			for (int dx = 0; dx < padHiW; dx++)
			{
				double x0 = dx - dstCx;
				int srcX = (int)Math.Round(srcCx + x0 * cosA - y0 * sinA);
				int srcY = (int)Math.Round(srcCy + x0 * sinA + y0 * cosA);

				int destIdx = (dy * padHiW + dx) * 4;
				if (srcX >= 0 && srcX < hiW && srcY >= 0 && srcY < hiH)
				{
					int srcIdx = (srcY * hiW + srcX) * 4;
					Buffer.BlockCopy(hiRes, srcIdx, rotatedPad8x, destIdx, 4);
				}
			}
		}

		// === Stage 3: 8×8 block voting downsample to padded canvas ===
		int padW = w * 2;
		int padH = h * 2;
		byte[] padResult = new byte[padW * padH * 4];
		// For each pixel in the padded canvas, vote over the corresponding 8×8 block in the rotated high‑res image
		var colorCounts = new System.Collections.Generic.Dictionary<int, int>(64);
		for (int y = 0; y < padH; y++)
		{
			for (int x = 0; x < padW; x++)
			{
				colorCounts.Clear();
				for (int by = 0; by < 8; by++)
				{
					int srcY = y * 8 + by;
					if (srcY >= padHiH) continue;
					int rowBase = srcY * padHiW * 4;
					for (int bx = 0; bx < 8; bx++)
					{
						int srcX = x * 8 + bx;
						if (srcX >= padHiW) continue;
						int srcIdx = rowBase + srcX * 4;
						// transparent pixels vote as key 0; this lets them outvote sparse
						// outline pixels at block edges, keeping diagonal outlines thin
						byte a = rotatedPad8x[srcIdx + 3];
						int key = a == 0 ? 0 :
							((a << 24) |
							(rotatedPad8x[srcIdx + 2] << 16) |
							(rotatedPad8x[srcIdx + 1] << 8) |
							rotatedPad8x[srcIdx]);
						if (colorCounts.TryGetValue(key, out var cnt))
							colorCounts[key] = cnt + 1;
						else
							colorCounts[key] = 1;
					}
				}
				// pick most frequent color
				int bestKey = 0;
				int bestCount = -1;
				foreach (var kvp in colorCounts)
				{
					if (kvp.Value > bestCount)
					{
						bestCount = kvp.Value;
						bestKey = kvp.Key;
					}
				}
				int dstIdx = (y * padW + x) * 4;
				padResult[dstIdx] = (byte)(bestKey & 0xFF);
				padResult[dstIdx + 1] = (byte)((bestKey >> 8) & 0xFF);
				padResult[dstIdx + 2] = (byte)((bestKey >> 16) & 0xFF);
				padResult[dstIdx + 3] = (byte)((bestKey >> 24) & 0xFF);
			}
		}

		// === Stage 4: Find bounding box of visible rotated pixels on padded canvas ===
		int minX = int.MaxValue, maxX = int.MinValue;
		int minY = int.MaxValue, maxY = int.MinValue;

		for (int y = 0; y < padH; y++)
		{
			for (int x = 0; x < padW; x++)
			{
				if (padResult[(y * padW + x) * 4 + 3] >= 128)
				{
					if (x < minX) minX = x;
					if (x > maxX) maxX = x;
					if (y < minY) minY = y;
					if (y > maxY) maxY = y;
				}
			}
		}

		// If no visible pixels, return empty
		if (minX > maxX || minY > maxY)
			return new byte[w * h * 4];

		// === Stage 5: Center the rotated pixels in the original w×h canvas ===
		int newStartX = (padW - w) / 2;
		int newStartY = (padH - h) / 2;

		// === Stage 6: Copy cropped/shifted region into final w × h canvas ===
		byte[] result = new byte[w * h * 4];
		for (int y = 0; y < h; y++)
		{
			int srcY = newStartY + y;
			if (srcY < 0 || srcY >= padH) continue;

			for (int x = 0; x < w; x++)
			{
				int srcX = newStartX + x;
				if (srcX < 0 || srcX >= padW) continue;

				int srcIdx = (srcY * padW + srcX) * 4;
				int dstIdx = (y * w + x) * 4;
				CopyPixel(padResult, srcIdx, result, dstIdx);
			}
		}

		// === Stage 7: Strict binary alpha ===
		for (int i = 0; i < result.Length; i += 4)
		{
			if (result[i + 3] < 128)
			{
				result[i] = 0;
				result[i + 1] = 0;
				result[i + 2] = 0;
				result[i + 3] = 0;
			}
			else
			{
				result[i + 3] = 255;
			}
		}

		// === Stage 8: RotSprite detail restoration & orphan cleanup ===
		result = RestoreDetails(result, srcRgba, w, h, angleDegrees);
		return CleanOrphans(result, w, h);
	}

	public static byte[] RestoreDetails(byte[] result, byte[] srcRgba, int w, int h, double angleDegrees)
	{
		byte[] restored = (byte[])result.Clone();

		double cx = (w - 1) / 2.0;
		double cy = (h - 1) / 2.0;
		double rad = -angleDegrees * Math.PI / 180.0;
		double cosA = Math.Cos(rad);
		double sinA = Math.Sin(rad);

		ReadOnlySpan<(int dx, int dy)> cardinals = stackalloc (int, int)[]
		{
			(0, -1), (0, 1), (-1, 0), (1, 0)
		};

		for (int y = 1; y < h - 1; y++)
		{
			for (int x = 1; x < w - 1; x++)
			{
				int idx = (y * w + x) * 4;
				if (result[idx + 3] == 0) continue;

				double x0 = x - cx;
				double y0 = y - cy;
				int srcX = (int)Math.Round(cx + x0 * cosA - y0 * sinA);
				int srcY = (int)Math.Round(cy + x0 * sinA + y0 * cosA);

				if (srcX < 0 || srcX >= w || srcY < 0 || srcY >= h)
					continue;

				int srcIdx = (srcY * w + srcX) * 4;
				if (srcRgba[srcIdx + 3] == 0)
					continue;

				if (result[idx] == srcRgba[srcIdx] &&
					result[idx + 1] == srcRgba[srcIdx + 1] &&
					result[idx + 2] == srcRgba[srcIdx + 2])
					continue;

				int sameCount = 0;
				int totalVisible = 0;

				foreach (var (ddx, ddy) in cardinals)
				{
					int nIdx = ((y + ddy) * w + (x + ddx)) * 4;
					if (result[nIdx + 3] == 0) continue;
					totalVisible++;
					if (result[nIdx] == result[idx] &&
						result[nIdx + 1] == result[idx + 1] &&
						result[nIdx + 2] == result[idx + 2])
						sameCount++;
				}

				if (sameCount >= 3 && totalVisible >= 3)
				{
					restored[idx] = srcRgba[srcIdx];
					restored[idx + 1] = srcRgba[srcIdx + 1];
					restored[idx + 2] = srcRgba[srcIdx + 2];
					restored[idx + 3] = 255;
				}
			}
		}

		return restored;
	}

	public static bool PixelsSimilar(byte[] buf, int idx1, byte[] buf2, int idx2)
	{
		byte a1 = buf[idx1 + 3], a2 = buf2[idx2 + 3];

		if (a1 == 0 && a2 == 0) return true;
		if (a1 == 0 || a2 == 0) return false;

		int dr = buf[idx1] - buf2[idx2];
		int dg = buf[idx1 + 1] - buf2[idx2 + 1];
		int db = buf[idx1 + 2] - buf2[idx2 + 2];

		int dist = dr * dr + 2 * dg * dg + db * db;
		return dist < 9216;
	}

	public static void CopyPixel(byte[] src, int srcIdx, byte[] dst, int dstIdx)
	{
		dst[dstIdx] = src[srcIdx];
		dst[dstIdx + 1] = src[srcIdx + 1];
		dst[dstIdx + 2] = src[srcIdx + 2];
		dst[dstIdx + 3] = src[srcIdx + 3];
	}

	public static byte[] CleanOrphans(byte[] imgRgba, int w, int h)
	{
		byte[] cleaned = (byte[])imgRgba.Clone();

		for (int y = 1; y < h - 1; y++)
		{
			for (int x = 1; x < w - 1; x++)
			{
				int idx = (y * w + x) * 4;
				if (imgRgba[idx + 3] == 0) continue;

				int neighborCount = 0;
				for (int ny = y - 1; ny <= y + 1; ny++)
				{
					for (int nx = x - 1; nx <= x + 1; nx++)
					{
						if (ny == y && nx == x) continue;
						if (imgRgba[(ny * w + nx) * 4 + 3] > 0)
						{
							neighborCount++;
						}
					}
				}

				if (neighborCount <= 1)
				{
					cleaned[idx] = 0;
					cleaned[idx + 1] = 0;
					cleaned[idx + 2] = 0;
					cleaned[idx + 3] = 0;
				}
			}
		}

		return cleaned;
	}

	public static byte[] FlipVertical(byte[] src, int w, int h)
	{
		byte[] dest = new byte[src.Length];
		for (int y = 0; y < h; y++)
		{
			int srcRow = y * w * 4;
			int destRow = (h - 1 - y) * w * 4;
			Buffer.BlockCopy(src, srcRow, dest, destRow, w * 4);
		}
		return dest;
	}

	public static byte[] FlipHorizontal(byte[] src, int w, int h)
	{
		byte[] dest = new byte[src.Length];
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				int srcIdx = (y * w + x) * 4;
				int destIdx = (y * w + (w - 1 - x)) * 4;
				Buffer.BlockCopy(src, srcIdx, dest, destIdx, 4);
			}
		}
		return dest;
	}

	public static byte[] RotateRgba90(byte[] src, int w, int h, int steps)
	{
		steps = (steps % 4 + 4) % 4;
		if (steps == 0) return (byte[])src.Clone();

		int newW = (steps % 2 == 1) ? h : w;
		int newH = (steps % 2 == 1) ? w : h;
		byte[] dest = new byte[newW * newH * 4];

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				int srcIdx = (y * w + x) * 4;
				int newX = x;
				int newY = y;

				switch (steps)
				{
					case 1: // 90° clockwise
						newX = h - 1 - y;
						newY = x;
						break;
					case 2: // 180°
						newX = w - 1 - x;
						newY = h - 1 - y;
						break;
					case 3: // 270° clockwise (90° counter-clockwise)
						newX = y;
						newY = w - 1 - x;
						break;
				}

				int destIdx = (newY * newW + newX) * 4;
				Buffer.BlockCopy(src, srcIdx, dest, destIdx, 4);
			}
		}

		return dest;
	}
}
