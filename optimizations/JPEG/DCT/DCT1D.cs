using System;
using JPEG.Processor;

namespace JPEG;

public class DCT1D
{
    private const int N = 8;
    // единая таблица предрасчетов для DCT и IDCT.
    // размер 64, индекс считается как: (Строка * Ширина + Колонка)
    private static readonly float[] PrecalcTable = new float[N * N];
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


    static DCT1D()
    {
        double beta = 1d / N + 1d / N; // 0.25
        double sqrtBeta = Math.Sqrt(beta); // 0.5
        double alpha = 1 / Math.Sqrt(2);

        for (int freq = 0; freq < N; freq++)
        {
            double flag = (freq == 0) ? alpha : 1.0;
            
            for (int space = 0; space < N; space++)
            {
                double cosVal = Math.Cos((2d * space + 1d) * freq * Math.PI / (2 * N));
                
                // предрассчитываем косинус вместе со всеми флагами и бетой
                // для 1D массива используем формулу: (Строка * Ширина + Колонка)
                PrecalcTable[freq * N + space] = (float)(cosVal * flag * sqrtBeta);
            }
        }
        for (int i = 0; i < 64; i++)
        {
            InvLumaQ[i] = 1.0f / LumaQuantMatrix[i];
            InvChromaQ[i] = 1.0f / ChromaQuantMatrix[i];
        }
    }
    
    // вместо возвращения нового массива, пишем результат в переданный массив `coeffs`.
    // input и coeffs должны иметь размер 64!
    public static void DCT(float[] temp, byte[] input, byte[] output, short shift = -128, bool isChroma = false)
    {
        float[] invQ = isChroma ? InvChromaQ : InvLumaQ;

        // проход по строкам (Spatial X -> Frequency U)
        for (int y = 0; y < N; y++)
        {
            int yOffset = y * N;
            for (int u = 0; u < N; u++)
            {
                float sum = 0f;
                int uOffset = u * N;
                for (int x = 0; x < N; x++)
                {
                    // input[y, x] и PrecalcTable[u, x]
                    sum += (input[yOffset + x] + shift) * PrecalcTable[uOffset + x];
                }
                // записываем результат: temp[y, u]
                temp[yOffset + u] = sum;
            }
        }

        // проход по столбцам (Spatial Y -> Frequency V)
        for (int u = 0; u < N; u++)
        {
            for (int v = 0; v < N; v++)
            {
                float sum = 0f;
                int vOffset = v * N;
                for (int y = 0; y < N; y++)
                {
                    // temp[y, u] и PrecalcTable[v, y]
                    sum += temp[y * N + u] * PrecalcTable[vOffset + y];
                }
                // записываем результат: coeffs[v, u]
                output[vOffset + u] = (byte)(sum * invQ[vOffset + u]);
            }
        }
    }

    public static void IDCT(float[] temp, byte[] input, short[] output, short shift = 128, bool isChroma = false)
    {
        byte[] qMatrix = isChroma ? ChromaQuantMatrix : LumaQuantMatrix;

        // обратный проход по столбцам (Frequency V -> Spatial Y)
        for (int u = 0; u < N; u++)
        {
            for (int y = 0; y < N; y++)
            {
                float sum = 0f;
                for (int v = 0; v < N; v++)
                {
                    // coeffs[v, u] и PrecalcTable[v, y]
                    sum += (short)((sbyte)input[v * N + u] * qMatrix[v * N + u]) * PrecalcTable[v * N + y];
                }
                // temp[y, u]
                temp[y * N + u] = sum;
            }
        }

        // обратный проход по строкам (Frequency U -> Spatial X)
        for (int y = 0; y < N; y++)
        {
            int yOffset = y * N;
            for (int x = 0; x < N; x++)
            {
                float sum = 0f;
                for (int u = 0; u < N; u++)
                {
                    // temp[y, u] и PrecalcTable[u, x]
                    sum += temp[yOffset + u] * PrecalcTable[u * N + x];
                }
                // output[y, x]
                output[yOffset + x] = (short)(sum + shift);
            }
        }
    }
}