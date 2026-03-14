using System;
using System.Drawing.Imaging;

namespace JPEG.Processor;

public static class FastBitmap
{
	public static unsafe void ExtractBlock(
		BitmapData bmpData, int startX, int startY,
		byte[] Y0, byte[] Y1, byte[] Y2, byte[] Y3, byte[] Cb, byte[] Cr)
	{
		// длинна строки bitmap
		int stride = bmpData.Stride;
		// начало bitmap левый верхний угол
		byte* ptr = (byte*)bmpData.Scan0;
		
		int sumCb, sumCr;
		// идем по блоку 16 на 16
		for (int dy = 0; dy < 16; dy += 2)
		{
			for (int dx = 0; dx < 16; dx += 2)
			{
				sumCb = 0;
				sumCr = 0;

				// читаем квадратик 2 на 2 пикселя
				for (int sy = 0; sy < 2; sy++)
				{
					for (int sx = 0; sx < 2; sx++)
					{
						int py = startY + dy + sy;
						int px = startX + dx + sx;

						// вычисляем адрес пикселя в памяти (Format24bpp: 3 байта на пиксель)
						byte* pixel = ptr + py * stride + px * 3;

						int B = pixel[0];
						int G = pixel[1];
						int R = pixel[2];

						// перевод в YCbCr
						// ограничиваем от 0 до 255 на всякий случай
						byte yVal = ToByte((77 * R + 150 * G + 29 * B) >> 8);
						int cbVal = ((-43 * R - 85 * G + 128 * B) >> 8) + 128;
						int crVal = ((128 * R - 107 * G - 21 * B) >> 8) + 128;


						// куда положить Y - в один из 4 блоков
						int blockY = (dy + sy) % 8;
						int blockX = (dx + sx) % 8;
						int flatIndex = blockY * 8 + blockX;

						if (dy < 8 && dx < 8) Y0[flatIndex] = yVal;
						else if (dy < 8 && dx >= 8) Y1[flatIndex] = yVal;
						else if (dy >= 8 && dx < 8) Y2[flatIndex] = yVal;
						else Y3[flatIndex] = yVal;

						// накапливаем сумму для цвета
						sumCb += cbVal;
						sumCr += crVal;
					}
				}

				// усредняем цвет для блока 2x2 (делим на 4) и кладем в массив цвета
				int chromaFlatIndex = (dy / 2) * 8 + (dx / 2);
				Cb[chromaFlatIndex] = ToByte(sumCb >> 2);
				Cr[chromaFlatIndex] = ToByte(sumCr >> 2);
			}
		}
	}
	
    public static unsafe void WriteBlock(
	    BitmapData bmpData, int startX, int startY, 
	    short[] Y0, short[] Y1, short[] Y2, short[] Y3, short[] Cb, short[] Cr)
	{
		// длинна строки bitmap
		int stride = bmpData.Stride;
		// начало bitmap левый верхний угол
		byte* scan0 = (byte*)bmpData.Scan0.ToPointer();
		
		// проходим по квадрату 16x16
	    for (int dy = 0; dy < 16; dy++)
	    {
	        // находим адрес строки в памяти Bitmap
	        byte* row = scan0 + (startY + dy) * stride;

	        for (int dx = 0; dx < 16; dx++)
	        {
	            // ищем правильное значение яркости Y
	            int blockY = dy % 8;
	            int blockX = dx % 8;
	            int flatIndexY = blockY * 8 + blockX;
	            float yVal;

	            if (dy < 8 && dx < 8) yVal = Y0[flatIndexY];
	            else if (dy < 8 && dx >= 8) yVal = Y1[flatIndexY];
	            else if (dy >= 8 && dx < 8) yVal = Y2[flatIndexY];
	            else yVal = Y3[flatIndexY];

	            // ищем правильное значение цвета (Cb, Cr)
	            // так как цвет сжат в 2 раза, мы делим координаты на 2
	            // таким образом 4 пикселя (квадрат 2 на 2) получат один и тот же цвет.
	            int chromaFlatIndex = (dy / 2) * 8 + (dx / 2);
	            float cbVal = Cb[chromaFlatIndex] - 128;
	            float crVal = Cr[chromaFlatIndex] - 128;

	            // быстрый перевод YCbCr -> RGB
	            int Y = (int)yVal;
	            int CbInt = (int)cbVal;
	            int CrInt = (int)crVal;

	            byte* pixel = row + (startX + dx) * 3;
	            // записываем в память (Format24bppRgb хранит как B, G, R)
	            // ограничиваем значения от 0 до 255
	            // из-за потерь при квантовании значения могут вылезти за пределы 0-255.
	            pixel[0] = ToByte(Y + ((454 * CbInt) >> 8));
	            pixel[1] = ToByte(Y - ((88 * CbInt + 183 * CrInt) >> 8));
	            pixel[2] = ToByte(Y + ((359 * CrInt) >> 8));
	        }
	    }
	}

	private static byte ToByte(int x)
	{
		if (x < 0) return 0;
		else if (x > 255) return 255;
		return (byte)x;
	}
}