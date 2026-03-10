using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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
		var allQuantizedBytes = new List<byte>((int)(width * height * 1.5));

		BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
		float[] dctInput = new float[64];
		float[] dctOutput = new float[64];

		byte[] Y0 = new byte[64], Y1 = new byte[64], Y2 = new byte[64], Y3 = new byte[64];
		byte[] Cb = new byte[64], Cr = new byte[64];
		for (var y = 0; y < height; y += DCTSize)
		{
			for (var x = 0; x < width; x += DCTSize)
			{
				ExtractBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
				ProcessBlock(Y0, dctInput, dctOutput, allQuantizedBytes); // Левый верхний 8x8
				ProcessBlock(Y1, dctInput, dctOutput, allQuantizedBytes); // Правый верхний 8x8
				ProcessBlock(Y2, dctInput, dctOutput, allQuantizedBytes); // Левый нижний 8x8
				ProcessBlock(Y3, dctInput, dctOutput, allQuantizedBytes); // Правый нижний 8x8

				// 3. ОБРАБАТЫВАЕМ ЦВЕТ (Cb и Cr) - их всего по одному блоку на зону 16x16!
				// Внимание: для цвета должна применяться ДРУГАЯ таблица квантования (более жесткая)
				ProcessBlock(Cb, dctInput, dctOutput, allQuantizedBytes);
				ProcessBlock(Cr, dctInput, dctOutput, allQuantizedBytes);
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

	private void ProcessBlock(byte[] block, float[] input, float[] output, List<byte> outputList)
	{
		DCT1D.DCT(block, output);

		// Передаем флаг isChroma в твой метод квантования, 
		// потому что стандарт JPEG требует разных матриц квантования для Яркости и Цвета!
		var quantized = Quantizer.QuantizeAndZigZagScan(output);
		outputList.AddRange(quantized);
	}

	private unsafe void ExtractBlock(
		BitmapData bmpData, int startX, int startY,
		byte[] Y0, byte[] Y1, byte[] Y2, byte[] Y3, byte[] Cb, byte[] Cr)
	{
		int stride = bmpData.Stride;
		byte* ptr = (byte*)bmpData.Scan0;
		for (int dy = 0; dy < DCTSize; dy += 2)
		{
			for (int dx = 0; dx < DCTSize; dx += 2)
			{
				int sumCb = 0, sumCr = 0;

				// Читаем квадратик 2x2 пикселя
				for (int sy = 0; sy < 2; sy++)
				{
					for (int sx = 0; sx < 2; sx++)
					{
						int py = startY + dy + sy;
						int px = startX + dx + sx;

						// Вычисляем адрес пикселя в памяти (Format24bpp: 3 байта на пиксель)
						byte* pixel = ptr + py * stride + px * 3;

						int B = pixel[0];
						int G = pixel[1];
						int R = pixel[2];

						// Перевод в YCbCr (Быстрая целая арифметика)
						int yVal = (77 * R + 150 * G + 29 * B) >> 8;
						int cbVal = ((-43 * R - 85 * G + 128 * B) >> 8) + 128;
						int crVal = ((128 * R - 107 * G - 21 * B) >> 8) + 128;

						// Ограничиваем от 0 до 255 на всякий случай
						yVal = Math.Clamp(yVal, 0, 255);

						// Куда положить Y? В один из 4 блоков
						int blockY = (dy + sy) % 8;
						int blockX = (dx + sx) % 8;
						int flatIndex = blockY * 8 + blockX;

						if (dy < 8 && dx < 8) Y0[flatIndex] = (byte)yVal;
						else if (dy < 8 && dx >= 8) Y1[flatIndex] = (byte)yVal;
						else if (dy >= 8 && dx < 8) Y2[flatIndex] = (byte)yVal;
						else Y3[flatIndex] = (byte)yVal;

						// Накапливаем сумму для цвета
						sumCb += cbVal;
						sumCr += crVal;
					}
				}

				// Усредняем цвет для блока 2x2 (Делим на 4) и кладем в массив цвета
				int chromaFlatIndex = (dy / 2) * 8 + (dx / 2);
				Cb[chromaFlatIndex] = (byte)Math.Clamp(sumCb >> 2, 0, 255);
				Cr[chromaFlatIndex] = (byte)Math.Clamp(sumCr >> 2, 0, 255);
			}
		}
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
					WriteBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
				}
			}

			result.UnlockBits(bmpData);
		}

		return result;
	}

	private unsafe void WriteBlock(
	    BitmapData bmpData, int startX, int startY, 
	    float[] Y0, float[] Y1, float[] Y2, float[] Y3, float[] Cb, float[] Cr)
	{
		int stride = bmpData.Stride;
		byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
		// Проходим по квадрату 16x16
	    for (int dy = 0; dy < 16; dy++)
	    {
	        // Находим адрес строки в памяти Bitmap
	        byte* row = scan0 + (startY + dy) * stride;

	        for (int dx = 0; dx < 16; dx++)
	        {
	            // 1. Ищем правильное значение Яркости (Y)
	            int blockY = dy % 8;
	            int blockX = dx % 8;
	            int flatIndexY = blockY * 8 + blockX;
	            float yVal;

	            if (dy < 8 && dx < 8) yVal = Y0[flatIndexY];
	            else if (dy < 8 && dx >= 8) yVal = Y1[flatIndexY];
	            else if (dy >= 8 && dx < 8) yVal = Y2[flatIndexY];
	            else yVal = Y3[flatIndexY];

	            // 2. Ищем правильное значение Цвета (Cb, Cr)
	            // Так как цвет сжат в 2 раза, мы делим координаты на 2!
	            // Таким образом 4 пикселя (квадрат 2x2) получат один и тот же цвет.
	            int chromaFlatIndex = (dy / 2) * 8 + (dx / 2);
	            float cbVal = Cb[chromaFlatIndex] - 128f; // Отнимаем 128 по стандарту JPEG
	            float crVal = Cr[chromaFlatIndex] - 128f;

	            // 3. Быстрый перевод YCbCr -> RGB (Целочисленная аппроксимация для скорости)
	            int Y = (int)yVal;
	            int CbInt = (int)cbVal;
	            int CrInt = (int)crVal;

	            int R = Y + ((359 * CrInt) >> 8);
	            int G = Y - ((88 * CbInt + 183 * CrInt) >> 8);
	            int B = Y + ((454 * CbInt) >> 8);

	            // 4. Ограничиваем значения от 0 до 255 (ОБЯЗАТЕЛЬНО!)
	            // Из-за потерь при квантовании значения могут вылезти за пределы 0-255.
	            R = Math.Clamp(R, 0, 255);
	            G = Math.Clamp(G, 0, 255);
	            B = Math.Clamp(B, 0, 255);

	            // 5. Записываем в память (Format24bppRgb хранит как B, G, R)
	            byte* pixel = row + (startX + dx) * 3;
	            pixel[0] = (byte)B;
	            pixel[1] = (byte)G;
	            pixel[2] = (byte)R;
	        }
	    }
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