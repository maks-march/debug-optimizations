using System;

namespace JPEG.Processor;

public static class Quantizer
{
    private static readonly byte[] ZigZagMap = new byte[64]
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

    // 2. Стандартные матрицы квантования JPEG (Базовое качество 50%)
    private static readonly byte[] LumaQuantMatrix = new byte[64]
    {
        16, 11, 10, 16,  24,  40,  51,  61,
        12, 12, 14, 19,  26,  58,  60,  55,
        14, 13, 16, 24,  40,  57,  69,  56,
        14, 17, 22, 29,  51,  87,  80,  62,
        18, 22, 37, 56,  68, 109, 103,  77,
        24, 35, 55, 64,  81, 104, 113,  92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103,  99
    };

    private static readonly byte[] ChromaQuantMatrix = new byte[64]
    {
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99
    };
    
    private static readonly float[] InvLumaQ = new float[64];
    private static readonly float[] InvChromaQ = new float[64];
    
    static Quantizer()
    {
        // В математике деление (x / Q) работает долго. 
        // Мы предрассчитываем (1.0 / Q), чтобы в цикле делать быстрое умножение (x * InvQ).
        // ЗАМЕТКА: Здесь также можно применять фактор качества (Quality Factor).
        for (int i = 0; i < 64; i++)
        {
            InvLumaQ[i] = 1.0f / LumaQuantMatrix[i];
            InvChromaQ[i] = 1.0f / ChromaQuantMatrix[i];
        }
    }
    
    public static byte[] QuantizeAndZigZagScan(float[] coeffs, bool isChroma = false)
    {
        byte[] result = new byte[64]; 
        
        float[] invQ = isChroma ? InvChromaQ : InvLumaQ;

        for (int i = 0; i < 64; i++)
        {
            // Берем индекс пикселя по Зигзагу
            int flatIndex = ZigZagMap[i];

            // Берем коэффициент DCT и умножаем на обратное квантование (вместо деления)
            float coeff = coeffs[flatIndex] * invQ[flatIndex];

            // Округляем до ближайшего целого (используем быстрое приведение типов)
            // Примечание: Math.Round работает медленнее, поэтому для скорости делают так:
            // int quantizedVal = (int)(coeff + (coeff > 0 ? 0.5f : -0.5f));

            // Приводим к байту (ВАЖНО: убедись, что твой Хаффман ожидает byte, 
            // так как коэффициенты могут быть отрицательными! Обычно используют short).
            result[i] = (byte)coeff; 
        }

        return result;
    }

    public static void DequantizeAndZigZagUnscan(byte[] inputZigZag, int[] outputDct, bool isChroma = false)
    {
        byte[] qMatrix = isChroma ? ChromaQuantMatrix : LumaQuantMatrix;

        for (int i = 0; i < 64; i++)
        {
            // 1. Узнаем, куда (в какой плоский индекс y*8+x) нужно положить значение
            int flatIndex = ZigZagMap[i];

            // 2. Читаем квантованное значение. 
            // ВАЖНО: Коэффициенты DCT могут быть отрицательными! 
            // Если твой Хаффман выдает byte[], то отрицательные числа там хранятся 
            // в дополнительном коде (например, -1 это 255). 
            // Нам ОБЯЗАТЕЛЬНО нужно привести это к signed (знаковому) типу sbyte, 
            // иначе деквантование сломает цвета (сделает из -1 огромное число +255).
            // 3. Деквантование: Умножаем на оригинальную матрицу Q 
            // (так как при сжатии мы делили, тут мы умножаем обратно)
            outputDct[flatIndex] = (sbyte)inputZigZag[i] * qMatrix[flatIndex];
        }
    }
    
    #region old
    
    public const byte CompressionQuality = 70;
    private static int[,] QuantizationMatrix { get; set; } = GetQuantizationMatrix((int)CompressionQuality);
    
    private static byte[] QuantizeAndZigZagScan(float[,] channelFreqs)
    {
        var result = new byte[64];
        var quantizationMatrix = QuantizationMatrix;
		
        for (int i = 0; i < 64; i++)
        {
            int flatIndex = ZigZagMap[i];
        
            int y = flatIndex / 8;
            int x = flatIndex % 8;
            result[i] = (byte)(channelFreqs[y, x] / quantizationMatrix[y, x]);
        }

        return result;
    }
	
    public static int[,] DequantizeAndZigZagUnScan(byte[] quantizedBytes)
    {
        var result = new int[8,8];
        var quantizationMatrix = QuantizationMatrix;

        for (int i = 0; i < 64; i++)
        {
            int flatIndex = ZigZagMap[i];
            int y = flatIndex / 8;
            int x = flatIndex % 8;
            result[y, x] = ((sbyte)quantizedBytes[i] * quantizationMatrix[y, x]);
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
    

    #endregion
}