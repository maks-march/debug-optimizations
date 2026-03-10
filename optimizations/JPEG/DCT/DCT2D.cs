using System;

namespace JPEG;

public class DCT2D
{
	private const int N = 8;
	private static float[,] _temp = new float[N, N];
	private static readonly float[,] PrecalcDCT = new float[N, N];

	static DCT2D()
	{
		double beta = 1d / N + 1d / N;
		double alpha = 1 / Math.Sqrt(2);
        
		double sqrtBeta = Math.Sqrt(beta);

		for (int u = 0; u < N; u++)
		{
			double flagU = (u == 0) ? alpha : 1.0;
			for (int x = 0; x < N; x++)
			{
				double cosVal = Math.Cos((2d * x + 1d) * u * Math.PI / (2 * N));
                
				PrecalcDCT[u, x] = (float)(cosVal * flagU * sqrtBeta);
			}
		}
	}
	
	
	
	public static float[,] DCT(byte[,] input, short shift = -128)
	{
		var coeffs = new float[N, N];
		Array.Clear(_temp);
		float sum;

		for (byte y = 0; y < N; y++)
		{
			for (byte u = 0; u < N; u++)
			{
				sum = 0f;
				for (byte x = 0; x < N; x++)
				{
					sum += (input[x, y] + shift) * PrecalcDCT[u, x];
				}
				_temp[u, y] = sum;
			}
		}

		for (byte u = 0; u < N; u++)
		{
			for (byte v = 0; v < N; v++)
			{
				sum = 0f;
				for (byte y = 0; y < N; y++)
				{
					sum += _temp[u, y] * PrecalcDCT[v, y];
				}
				coeffs[u, v] = sum;
			}
		}

		return coeffs;
	}

	public static void IDCT(int[,] coeffs, float[,] output, short shift = 128)
	{
		float sum;
		for (byte x = 0; x < N; x++)
		{
			for (byte v = 0; v < N; v++)
			{
				sum = 0f;
				for (byte u = 0; u < N; u++)
				{
					sum += coeffs[u, v] * PrecalcDCT[u, x];
				}
				_temp[x, v] = sum;
			}
		}

		for (byte x = 0; x < N; x++)
		{
			for (byte y = 0; y < N; y++)
			{
				sum = 0f;
				for (byte v = 0; v < N; v++)
				{
					sum += _temp[x, v] * PrecalcDCT[v, y];
				}
				output[x, y] = sum + shift;
			}
		}
	}
}