using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace JPEG.Processor;

public class JpegProcessor : IJpegProcessor
{
	public static readonly JpegProcessor Init = new();
	public const int CompressionQuality = 70;
	private const int DCTSize = 8;
	private int[,] QuantizationMatrix { get; set; } = GetQuantizationMatrix(CompressionQuality);
	
	private static readonly byte[] ZigZagMap = new byte[]
	{
		0,  1,  8, 16,  9,  2,  3, 10,
		17, 24, 32, 25, 18, 11,  4,  5,
		12, 19, 26, 33, 40, 48, 41, 34,
		27, 20, 13,  6,  7, 14, 21, 28,
		35, 42, 49, 56, 57, 50, 43, 36,
		29, 22, 15, 23, 30, 37, 44, 51,
		58, 59, 52, 45, 38, 31, 39, 46,
		53, 60, 61, 54, 47, 55, 62, 63
	};

	public void Compress(string imagePath, string compressedImagePath)
	{
		using var fileStream = File.OpenRead(imagePath);
		using var bmp = (Bitmap)Image.FromStream(fileStream, false, false);
		//Console.WriteLine($"{bmp.Width}x{bmp.Height} - {fileStream.Length / (1024.0 * 1024):F2} MB");
		var compressionResult = Compress(bmp, CompressionQuality);
		compressionResult.Save(compressedImagePath);
	}

	public void Uncompress(string compressedImagePath, string uncompressedImagePath)
	{
		var compressedImage = CompressedImage.Load(compressedImagePath);
		var uncompressedImage = Uncompress(compressedImage);
		var resultBmp = (Bitmap)uncompressedImage;
		resultBmp.Save(uncompressedImagePath, ImageFormat.Bmp);
	}

	private CompressedImage Compress(Bitmap bmp, int quality = 50)
	{
		var allQuantizedBytes = new List<byte>();

		var height = bmp.Height - bmp.Height % 8;
		var width = bmp.Width - bmp.Width % 8;
		var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
		BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
		for (var y = 0; y < height; y += DCTSize)
		{
			for (var x = 0; x < width; x += DCTSize)
			{
				for (int i = 0; i < 3; i++)
				{
					var subMatrix = GetSubMatrix(bmpData, y, 8, x, 8, i);
					var channelFreqs = DCT.DCT2D(subMatrix);
					allQuantizedBytes.AddRange(QuantizeAndZigZagScan(channelFreqs));
				}
			}
		}
		bmp.UnlockBits(bmpData);
		
		long bitsCount;
		Dictionary<BitsWithLength, byte> decodeTable;
		var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out decodeTable, out bitsCount);

		return new CompressedImage
		{
			Quality = quality, CompressedBytes = compressedBytes, BitsCount = bitsCount, DecodeTable = decodeTable,
			Height = bmp.Height, Width = bmp.Width
		};
	}

	private void ProccessSubMatrix(double[,] subMatrix, ref List<byte> allQuantizedBytes)
	{

	}

	private Bitmap Uncompress(CompressedImage image)
	{
		var height = image.Height - image.Height % 8;
		var width = image.Width - image.Width % 8;
		var result = new Bitmap(width, height);
		var rect = new Rectangle(0, 0, result.Width, result.Height);
		BitmapData bmpData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
		using (var allQuantizedBytes =
		       new MemoryStream(HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount)))
		{
			if (image.Quality != CompressionQuality)
				QuantizationMatrix = GetQuantizationMatrix(image.Quality);
			for (var y = 0; y < height; y += DCTSize)
			{
				for (var x = 0; x < width; x += DCTSize)
				{
					var r = new double[DCTSize, DCTSize];
					var g = new double[DCTSize, DCTSize];
					var b = new double[DCTSize, DCTSize];
					foreach (var channel in new[] { r, g, b })
					{
						var quantizedBytes = new byte[DCTSize * DCTSize];
						allQuantizedBytes.ReadAsync(quantizedBytes, 0, quantizedBytes.Length).Wait();
						var channelFreqs = DequantizeAndZigZagUnScan(quantizedBytes);

						DCT.IDCT2D(channelFreqs, channel);
					}

					SetPixels(bmpData, r, g, b, y, x);
				}
			}
		}
		result.UnlockBits(bmpData);
		return result;
	}
	
	private static unsafe double[,] GetSubMatrix(
		BitmapData bmpData, 
		int yOffset, int yLength, 
		int xOffset, int xLength,
		int componentOffset) 
	{
		var result = new double[yLength, xLength];
    
		byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
		int stride = bmpData.Stride;
		int bytesPerPixel = Image.GetPixelFormatSize(bmpData.PixelFormat) / 8;

		for (var j = 0; j < yLength; j++)
		{
			byte* row = scan0 + (yOffset + j) * stride;

			for (var i = 0; i < xLength; i++)
			{
				byte* pixel = row + (xOffset + i) * bytesPerPixel;

				result[j, i] = pixel[componentOffset];
			}
		}

		return result;
	}
	
	private static unsafe void SetPixels(
		BitmapData bmpData, 
		double[,] a, 
		double[,] b, 
		double[,] c, 
		int yOffset, 
		int xOffset)
	{
		var height = a.GetLength(0);
		var width = a.GetLength(1);

		int stride = bmpData.Stride;
    
		byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
    
		int bytesPerPixel = 3; 

		for (var y = 0; y < height; y++)
		{
			byte* row = scan0 + (yOffset + y) * stride;

			for (var x = 0; x < width; x++)
			{
				byte* pixel = row + (xOffset + x) * bytesPerPixel;

				pixel[0] = ToByte(c[y, x]);
				pixel[1] = ToByte(b[y, x]);
				pixel[2] = ToByte(a[y, x]);
			}
		}
	}
	
	public static byte ToByte(double d)
	{
		var val = (int)d;
		if (val > byte.MaxValue)
			return byte.MaxValue;
		if (val < byte.MinValue)
			return byte.MinValue;
		return (byte)val;
	}

	private byte[] QuantizeAndZigZagScan(double[,] channelFreqs)
	{
		var result = new byte[64];

		for (int i = 0; i < 64; i++)
		{
			int flatIndex = ZigZagMap[i];
        
			int y = flatIndex / 8;
			int x = flatIndex % 8;
			result[i] = (byte)(channelFreqs[y, x] / QuantizationMatrix[y, x]);
		}

		return result;
	}
	
	private double[,] DequantizeAndZigZagUnScan(byte[] quantizedBytes)
	{
		var result = new double[8,8];

		for (int i = 0; i < 64; i++)
		{
			int flatIndex = ZigZagMap[i];
			int y = flatIndex / 8;
			int x = flatIndex % 8;
			result[y, x] = ((sbyte)quantizedBytes[i] * QuantizationMatrix[y, x]);
		}

		return result;
	}

	private static int[,] GetQuantizationMatrix(int quality)
	{
		if (quality < 1 || quality > 99)
			throw new ArgumentException("quality must be in [1,99] interval");

		var multiplier = quality < 50 ? 5000 / quality : 200 - 2 * quality;

		var result = new[,]
		{
			{ 16, 11, 10, 16, 24, 40, 51, 61 },
			{ 12, 12, 14, 19, 26, 58, 60, 55 },
			{ 14, 13, 16, 24, 40, 57, 69, 56 },
			{ 14, 17, 22, 29, 51, 87, 80, 62 },
			{ 18, 22, 37, 56, 68, 109, 103, 77 },
			{ 24, 35, 55, 64, 81, 104, 113, 92 },
			{ 49, 64, 78, 87, 103, 121, 120, 101 },
			{ 72, 92, 95, 98, 112, 100, 103, 99 }
		};

		for (int y = 0; y < result.GetLength(0); y++)
		{
			for (int x = 0; x < result.GetLength(1); x++)
			{
				result[y, x] = (multiplier * result[y, x] + 50) / 100;
			}
		}

		return result;
	}
}