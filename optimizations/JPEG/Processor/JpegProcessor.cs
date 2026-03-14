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
			// хранит результат выполнения DCT
			float[] dctOutput = new float[64];
			
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
				ProcessBlock(Y0, dctOutput, rowBytes); // левый верхний 8x8
				ProcessBlock(Y1, dctOutput, rowBytes); // правый верхний 8x8
				ProcessBlock(Y2, dctOutput, rowBytes); // левый нижний 8x8
				ProcessBlock(Y3, dctOutput, rowBytes); // правый нижний 8x8

				// обрабатываем Cb и Cr
				// для цвета применяется другая матрица квантования
				ProcessBlock(Cb, dctOutput, rowBytes, isChroma: true);
				ProcessBlock(Cr, dctOutput, rowBytes, isChroma: true);
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

	private void ProcessBlock(byte[] block, float[] output, List<byte> outputList, bool isChroma = false)
	{
		// выполняем дискретное косинусное преобразование
		DCT1D.DCT(block, output);
		// размещаем байты зигзагом и делим на матрицу квантования
		// передаем флаг isChroma в метод квантования
		var quantized = Quantizer.QuantizeAndZigZagScan(output, isChroma);
		outputList.AddRange(quantized);
	}

	private Bitmap Uncompress(CompressedImage image)
	{
		// будем обрабатывать блоками по 16, нужно не выйти за рамки изображения
		var height = image.Height - image.Height % DCTSize;
		var width = image.Width - image.Width % DCTSize;
		var result = new Bitmap(width, height);
		// получаем поток из декодированных байтов, чтобы не хранить их все сразу
		using (var stream =
		       new MemoryStream(HuffmanCodec.Decode(image.CompressedBytes, image.DecodeTable, image.BitsCount)))
		{
			// получаем BitmapData для быстрого взаимодействия через указатели
			var rect = new Rectangle(0, 0, result.Width, result.Height);
			BitmapData bmpData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
			
			// буфер для чтения из потока
			byte[] buffer = new byte[64]; 
			// буфер для деквантования
			short[] coeffs = new short[64];
			
			// яркости для каждого пикселя блоков 8 на 8,
			// и усредненные разности цветов для блока пикселей 2 на 2
			float[] Y0 = new float[64], Y1 = new float[64], Y2 = new float[64], Y3 = new float[64];
			float[] Cb = new float[64], Cr = new float[64];
			for (var y = 0; y < height; y += DCTSize)
			{
				for (var x = 0; x < width; x += DCTSize)
				{
					// читаем и декодируем блоки яркости Y
					DecodeBlock(stream, buffer, coeffs, Y0, isChroma: false);
					DecodeBlock(stream, buffer, coeffs, Y1, isChroma: false);
					DecodeBlock(stream, buffer, coeffs, Y2, isChroma: false);
					DecodeBlock(stream, buffer, coeffs, Y3, isChroma: false);

					// читаем и декодируем блоки цвета Cb, Cr
					DecodeBlock(stream, buffer, coeffs, Cb, isChroma: true);
					DecodeBlock(stream, buffer, coeffs, Cr, isChroma: true);

					// собираем макроблок 16x16, переводим YCbCr->RGB и рисуем в Bitmap
					FastBitmap.WriteBlock(bmpData, x, y, Y0, Y1, Y2, Y3, Cb, Cr);
				}
			}

			result.UnlockBits(bmpData);
		}

		return result;
	}

	private void DecodeBlock(Stream stream, byte[] buffer, short[] coeffsTemp, float[] outputPixels, bool isChroma)
	{
		// чтение 64 байт из потока
		stream.Read(buffer, 0, 64);
		// распутываем зигзаг и умножаем на матрицу квантования
		// передаем флаг isChroma в метод квантования
		Quantizer.DequantizeAndZigZagUnscan(buffer, coeffsTemp, isChroma);
		// выполняем обратное дискретное косинусное преобразование
		DCT1D.IDCT(coeffsTemp, outputPixels);
	}
}