using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace JPEG.Processor;

public class JpegProcessor : IJpegProcessor
{
	public static readonly JpegProcessor Init = new();
	public const int CompressionQuality = 70;
	private const int DCTSize = 16;

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
		var height = bmp.Height - bmp.Height % DCTSize;
		var width = bmp.Width - bmp.Width % DCTSize;
		var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
		
		BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		
		int rowsCount = height / DCTSize;
		int colsCount = width / DCTSize;
		List<byte>[] rowResults = new List<byte>[rowsCount];
		Parallel.For(0, rowsCount, rowIndex =>
		{
			int y = rowIndex * DCTSize;
			float[] dctOutput = new float[64];

			byte[] Y0 = new byte[64], Y1 = new byte[64], Y2 = new byte[64], Y3 = new byte[64];
			byte[] Cb = new byte[64], Cr = new byte[64];
			var rowBytes = new List<byte>(colsCount * 6 * 64);
			for (var x = 0; x < width; x += DCTSize)
			{
				FastBitmap.ExtractBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
				
				ProcessBlock(Y0, dctOutput, rowBytes); // Левый верхний 8x8
				ProcessBlock(Y1, dctOutput, rowBytes); // Правый верхний 8x8
				ProcessBlock(Y2, dctOutput, rowBytes); // Левый нижний 8x8
				ProcessBlock(Y3, dctOutput, rowBytes); // Правый нижний 8x8

				// 3. ОБРАБАТЫВАЕМ ЦВЕТ (Cb и Cr) - их всего по одному блоку на зону 16x16!
				// Внимание: для цвета должна применяться ДРУГАЯ таблица квантования (более жесткая)
				ProcessBlock(Cb, dctOutput, rowBytes);
				ProcessBlock(Cr, dctOutput, rowBytes);
			}
			rowResults[rowIndex] = rowBytes;
		});
		
		var allQuantizedBytes = new List<byte>((int)(width * height * 1.5));
		for (int i = 0; i < rowsCount; i++)
		{
			allQuantizedBytes.AddRange(rowResults[i]);
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

	private void ProcessBlock(byte[] block, float[] output, List<byte> outputList)
	{
		DCT1D.DCT(block, output);
		// Передаем флаг isChroma в метод квантования, 
		// потому что стандарт JPEG требует разных матриц квантования для Яркости и Цвета!
		var quantized = Quantizer.QuantizeAndZigZagScan(output);
		outputList.AddRange(quantized);
	}

	private Bitmap Uncompress(CompressedImage image)
	{
		var height = image.Height - image.Height % 8;
		var width = image.Width - image.Width % 8;
		var result = new Bitmap(width, height);
		var rect = new Rectangle(0, 0, result.Width, result.Height);
		using (var stream =
		       new MemoryStream(HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount)))
		{
			BitmapData bmpData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

			byte[] buffer = new byte[64]; // Буфер для чтения из потока
			int[] coeffs = new int[64]; // Буфер для деквантования
			float[] Y0 = new float[64], Y1 = new float[64], Y2 = new float[64], Y3 = new float[64];
			float[] Cb = new float[64], Cr = new float[64];
			for (var y = 0; y < height; y += DCTSize)
			{
				for (var x = 0; x < width; x += DCTSize)
				{
					// 1. Читаем и декодируем 4 блока Яркости (Y)
					DecodeBlock(stream, buffer, coeffs, Y0, isChroma: false);
					DecodeBlock(stream, buffer, coeffs, Y1, isChroma: false);
					DecodeBlock(stream, buffer, coeffs, Y2, isChroma: false);
					DecodeBlock(stream, buffer, coeffs, Y3, isChroma: false);

					// 2. Читаем и декодируем 2 блока Цвета (Cb, Cr)
					DecodeBlock(stream, buffer, coeffs, Cb, isChroma: true);
					DecodeBlock(stream, buffer, coeffs, Cr, isChroma: true);

					// 3. Собираем макроблок 16x16, переводим YCbCr->RGB и рисуем в Bitmap
					FastBitmap.WriteBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
				}
			}

			result.UnlockBits(bmpData);
		}

		return result;
	}

	private void DecodeBlock(Stream stream, byte[] buffer, int[] coeffsTemp, float[] outputPixels, bool isChroma)
	{
		// Быстрое синхронное чтение 64 байт из потока
		stream.Read(buffer, 0, 64);
		// Распутываем зигзаг и умножаем на матрицу квантования
		Quantizer.DequantizeAndZigZagUnscan(buffer, coeffsTemp, isChroma);
		// Выполняем обратное дискретное косинусное преобразование
		// (Используй оптимизированный IDCT2D на 1D массивах!)
		DCT1D.IDCT(coeffsTemp, outputPixels);
	}
}