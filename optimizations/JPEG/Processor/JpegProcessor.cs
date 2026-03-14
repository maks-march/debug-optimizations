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
		var compressionResult = Compress(bmp, CompressionQuality);
		compressionResult.Save(compressedImagePath);
	}

	public void Uncompress(string compressedImagePath, string uncompressedImagePath)
	{
		var compressedImage = CompressedImage.Load(compressedImagePath);
		var uncompressedImage = Uncompress(compressedImage);
		var resultBmp = uncompressedImage;
		resultBmp.Save(uncompressedImagePath, ImageFormat.Bmp);
	}

	private CompressedImage Compress(Bitmap bmp, int quality = 50)
	{
		// будем обрабатывать блоками по 16, нужно не выйти за рамки изображения
		var height = bmp.Height - bmp.Height % DCTSize;
		var width = bmp.Width - bmp.Width % DCTSize;
		
		// получаем BitmapData для быстрого взаимодействия через указатели
		var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
		BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		
		// нужны для параллельного обхода по строкам
		int rowsCount = height / DCTSize;
		int colsCount = width / DCTSize;
		List<byte>[] rowResults = new List<byte>[rowsCount];
		
		Parallel.For(0, rowsCount, rowIndex =>
		{
			int y = rowIndex * DCTSize;
			// временный массив для хранения строк
			var temp = new float[64]; 
			// хранит результат выполнения DCT
			byte[] dctOutput = new byte[64];
			// яркости для каждого пикселя блоков 8 на 8,
			// и усредненные разности цветов для блока пикселей 2 на 2
			byte[] Y0 = new byte[64], Y1 = new byte[64], Y2 = new byte[64], Y3 = new byte[64];
			byte[] Cb = new byte[64], Cr = new byte[64];
			// накопитель результирующих байтов для строки
			var rowBytes = new List<byte>(colsCount * 6 * 64);
			for (var x = 0; x < width; x += DCTSize)
			{
				// получаем все данные из BitmapData
				FastBitmap.ExtractBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
				
				// обрабатываем блоки 8 на 8
				ProcessBlock(temp, Y0, dctOutput, rowBytes); // левый верхний 8x8
				ProcessBlock(temp, Y1, dctOutput, rowBytes); // правый верхний 8x8
				ProcessBlock(temp, Y2, dctOutput, rowBytes); // левый нижний 8x8
				ProcessBlock(temp, Y3, dctOutput, rowBytes); // правый нижний 8x8

				// обрабатываем Cb и Cr
				// для цвета применяется другая матрица квантования
				ProcessBlock(temp, Cb, dctOutput, rowBytes, isChroma: true);
				ProcessBlock(temp, Cr, dctOutput, rowBytes, isChroma: true);
			}
			// записываем полученные байты в свою ячейку
			rowResults[rowIndex] = rowBytes;
		});
		// совмещаем все результаты
		var allQuantizedBytes = new List<byte>((int)(width * height * 1.5));
		for (int i = 0; i < rowsCount; i++)
		{
			allQuantizedBytes.AddRange(rowResults[i]);
		}
		bmp.UnlockBits(bmpData);
		// кодируем байты
		long bitsCount;
		Dictionary<BitsWithLength, byte> decodeTable;
		var compressedBytes = HuffmanCodec.Encode(allQuantizedBytes, out decodeTable, out bitsCount);

		return new CompressedImage
		{
			Quality = quality, CompressedBytes = compressedBytes, BitsCount = bitsCount, DecodeTable = decodeTable,
			Height = bmp.Height, Width = bmp.Width
		};
	}

	private void ProcessBlock(float[] temp, byte[] block, byte[] output, List<byte> outputList, bool isChroma = false)
	{
		// выполняем дискретное косинусное преобразование
		DCT1D.DCT(temp, block, output, isChroma: isChroma);
		// размещаем байты зигзагом и делим на матрицу квантования
		// передаем флаг isChroma в метод квантования
		// var quantized = Quantizer.QuantizeAndZigZagScan(output, isChroma);
		outputList.AddRange(output);
	}

	private Bitmap Uncompress(CompressedImage image)
	{
		// будем обрабатывать блоками по 16, нужно не выйти за рамки изображения
		var height = image.Height - image.Height % DCTSize;
		var width = image.Width - image.Width % DCTSize;
		var result = new Bitmap(width, height);
		// декодируем все байты
		byte[] decodedBytes = HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount);
		// получаем BitmapData для быстрого взаимодействия через указатели
		var rect = new Rectangle(0, 0, result.Width, result.Height);
		BitmapData bmpData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
		
		// нужны для параллельного обхода по строкам
		int rowsCount = height / DCTSize;
		int colsCount = width / DCTSize;
		short bytesUsedInIteration = 64 * 6;
		Parallel.For(0, rowsCount, rowIndex =>
		{
			int y = rowIndex * DCTSize;
			// временный массив для хранения строк
			var temp = new float[64]; 
			// буфер для чтения из потока
			byte[] buffer = new byte[64];
		
			// яркости для каждого пикселя блоков 8 на 8,
			// и усредненные разности цветов для блока пикселей 2 на 2
			short[] Y0 = new short[64], Y1 = new short[64], Y2 = new short[64], Y3 = new short[64];
			short[] Cb = new short[64], Cr = new short[64];
			
			for (var x = 0; x < width; x += DCTSize)
			{
				// количество прочитанных до этой итерации байтов
				int offset = bytesUsedInIteration * (rowIndex * colsCount + x / DCTSize);
				// есть вероятность что байты вышли за структуру 16 на 16
				// читаем и декодируем блоки яркости Y
				DecodeBlock(temp, decodedBytes, buffer, Y0, offset);
				DecodeBlock(temp, decodedBytes, buffer, Y1, offset + 64);
				DecodeBlock(temp, decodedBytes, buffer, Y2, offset + 128);
				DecodeBlock(temp, decodedBytes, buffer, Y3, offset + 192);

				// читаем и декодируем блоки цвета Cb, Cr
				DecodeBlock(temp, decodedBytes, buffer, Cb, offset + 256);
				DecodeBlock(temp, decodedBytes, buffer, Cr, offset + 320);

				// собираем макроблок 16x16, переводим YCbCr->RGB и рисуем в Bitmap
				FastBitmap.WriteBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
			}
		});

		result.UnlockBits(bmpData);

		return result;
	}

	private void DecodeBlock(float[] temp, byte[] decodedBytes, byte[] buffer, short[] outputPixels, int offset, bool isChroma = false)
	{
		// чтение 64 байт из потока
		// stream.Read(buffer, offset, 64);
		// распутываем зигзаг и умножаем на матрицу квантования
		// передаем флаг isChroma в метод квантования
		// Quantizer.DequantizeAndZigZagUnscan(buffer, coeffsTemp, isChroma);
		// выполняем обратное дискретное косинусное преобразование
		Array.Fill(buffer, (byte)0);
		if (offset + 64 >= decodedBytes.Length)
			Array.Copy(decodedBytes, offset, buffer, 0, decodedBytes.Length - offset);
		else
			Array.Copy(decodedBytes, offset, buffer, 0, 64);
		DCT1D.IDCT(temp, buffer, outputPixels, isChroma: isChroma);
	}
}