using System;

namespace JPEG;

public class DCT1D
{
    private const int N = 8;
    private float[,] _temp = new float[N, N];
    // единая таблица предрасчетов для DCT и IDCT.
    // размер 64, индекс считается как: (Строка * Ширина + Колонка)
    private static readonly float[] PrecalcTable = new float[N * N];

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
    }
    
    // вместо возвращения нового массива, пишем результат в переданный массив `coeffs`.
    // input и coeffs должны иметь размер 64!
    public static void DCT(byte[] input, float[] coeffs, short shift = -128)
    {
        // временный массив для хранения строк
        var temp = new float[64]; 

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
                coeffs[vOffset + u] = sum;
            }
        }
    }

    public static void IDCT(short[] coeffs, float[] output, short shift = 128)
    {
        var temp = new float[64];

        // обратный проход по столбцам (Frequency V -> Spatial Y)
        for (int u = 0; u < N; u++)
        {
            for (int y = 0; y < N; y++)
            {
                float sum = 0f;
                for (int v = 0; v < N; v++)
                {
                    // coeffs[v, u] и PrecalcTable[v, y]
                    sum += coeffs[v * N + u] * PrecalcTable[v * N + y];
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
                output[yOffset + x] = sum + shift;
            }
        }
    }
}