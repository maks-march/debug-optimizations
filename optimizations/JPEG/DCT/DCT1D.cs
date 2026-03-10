using System;

namespace JPEG;

public class DCT1D
{
    private const int N = 8;
    private float[,] _temp = new float[N, N];
    // Единая таблица предрасчетов для DCT и IDCT.
    // Размер 64. Индекс считается как: (frequency * 8 + space)
    private static readonly float[] PrecalcTable = new float[N * N];

    static DCT1D()
    {
        double beta = 1d / N + 1d / N; // 0.25
        double sqrtBeta = Math.Sqrt(beta); // 0.5 (мы делим бету пополам для сепарабельности)
        double alpha = 1 / Math.Sqrt(2);

        for (int freq = 0; freq < N; freq++)
        {
            double flag = (freq == 0) ? alpha : 1.0;
            
            for (int space = 0; space < N; space++)
            {
                double cosVal = Math.Cos((2d * space + 1d) * freq * Math.PI / (2 * N));
                
                // Предрассчитываем косинус вместе со всеми флагами и бетой!
                // Для 1D массива используем формулу: (Строка * Ширину + Колонка)
                PrecalcTable[freq * N + space] = (float)(cosVal * flag * sqrtBeta);
            }
        }
    }
    
    // Вместо возвращения нового массива, мы пишем результат в переданный массив `coeffs`.
    // input и coeffs должны иметь размер 64!
    public static void DCT(byte[] input, float[] coeffs, short shift = -128)
    {
        // Временный массив для хранения строк. 
        // Если используешь C# 7.2+, замени на: Span<float> temp = stackalloc float[64];
        var temp = new float[64]; 

        // 1. Проход по СТРОКАМ (Spatial X -> Frequency U)
        for (int y = 0; y < N; y++)
        {
            int yOffset = y * N; // Оптимизация: считаем смещение строки 1 раз
            for (int u = 0; u < N; u++)
            {
                float sum = 0f;
                int uOffset = u * N;
                for (int x = 0; x < N; x++)
                {
                    // input[y, x] и PrecalcTable[u, x]
                    sum += (input[yOffset + x] + shift) * PrecalcTable[uOffset + x];
                }
                // Записываем результат: temp[y, u]
                temp[yOffset + u] = sum;
            }
        }

        // 2. Проход по СТОЛБЦАМ (Spatial Y -> Frequency V)
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
                // Записываем результат: coeffs[v, u] (где v - вертикальная частота, u - горизонтальная)
                coeffs[vOffset + u] = sum;
            }
        }
    }

    public static void IDCT(int[] coeffs, float[] output, short shift = 128)
    {
        var temp = new float[64]; // Аналогично, лучше использовать stackalloc float[64] если доступно

        // 1. Обратный проход по СТОЛБЦАМ (Frequency V -> Spatial Y)
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

        // 2. Обратный проход по СТРОКАМ (Frequency U -> Spatial X)
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