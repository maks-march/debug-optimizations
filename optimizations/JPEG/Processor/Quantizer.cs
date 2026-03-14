using System;

namespace JPEG.Processor;

public static class Quantizer
{
    public static readonly byte[] ZigZagMap = new byte[64]
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

    // стандартные матрицы квантования JPEG
    public static readonly byte[] LumaQuantMatrix = new byte[64]
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

    public static readonly byte[] ChromaQuantMatrix = new byte[64]
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

    public static readonly float[] InvLumaQ = new float[64];
    public static readonly float[] InvChromaQ = new float[64];


    static Quantizer()
    {
        // в математике деление (x / Q) работает долго. 
        // предрассчитываем (1.0 / Q), чтобы в цикле делать быстрое умножение (x * InvQ).
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
            // берем индекс пикселя по Зигзагу
            int flatIndex = ZigZagMap[i];

            // берем коэффициент DCT и умножаем на обратное квантование (вместо деления)
            byte coeff = (byte)(coeffs[i] * invQ[i]);

            result[i] = coeff; 
        }

        return result;
    }

    public static void DequantizeAndZigZagUnscan(byte[] inputZigZag, short[] outputDct, bool isChroma = false)
    {
        byte[] qMatrix = isChroma ? ChromaQuantMatrix : LumaQuantMatrix;

        for (int i = 0; i < 64; i++)
        {
            // узнаем, куда (в какой плоский индекс y*8+x) нужно положить значение
            int flatIndex = ZigZagMap[i];

            // читаем квантованное значение. 
            // коэффициенты DCT могут быть отрицательными
            // нужно привести их к знаковому типу sbyte,
            // иначе деквантование сломает цвета (сделает из -1 огромное число +255).
            // деквантование: умножаем на оригинальную матрицу Q
            outputDct[i] = (short)((sbyte)inputZigZag[i] * qMatrix[i]);
        }
    }
}