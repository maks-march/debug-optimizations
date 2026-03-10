using System;
using JPEG.Utilities;

namespace JPEG;

public class DCT
{
	private const int N = 8;
	private static readonly float[,] PrecalcDCT_X = new float[N, N];
	private static readonly float[,] PrecalcDCT_Y = new float[N, N];
	private static readonly float[,] PrecalcIDCT_X = new float[N, N];
	private static readonly float[,] PrecalcIDCT_Y = new float[N, N];

	static DCT()
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
                
				PrecalcDCT_X[u, x] = (float)(cosVal * flagU * sqrtBeta);
				PrecalcDCT_Y[u, x] = (float)(cosVal * 1.0 * sqrtBeta);

				PrecalcIDCT_X[u, x] = (float)(cosVal * flagU * sqrtBeta);
				PrecalcIDCT_Y[u, x] = (float)(cosVal * 1.0 * sqrtBeta);
			}
		}
	}
	
	public static float[,] DCT2D(byte[,] input, short shift = -128)
	{
		var coeffs = new float[N, N];
		var temp = new float[N, N];

		for (int y = 0; y < N; y++)
		{
			for (int u = 0; u < N; u++)
			{
				float sum = 0f;
				for (int x = 0; x < N; x++)
				{
					sum += (input[x, y] + shift) * PrecalcDCT_X[u, x];
				}
				temp[u, y] = sum;
			}
		}

		for (int u = 0; u < N; u++)
		{
			for (int v = 0; v < N; v++)
			{
				float sum = 0f;
				for (int y = 0; y < N; y++)
				{
					sum += temp[u, y] * PrecalcDCT_X[v, y];
				}
				coeffs[u, v] = sum;
			}
		}

		return coeffs;
	}

	public static void IDCT2D(int[,] coeffs, float[,] output, short shift = 128)
	{
		var temp = new float[N, N];

		for (int x = 0; x < N; x++)
		{
			for (int v = 0; v < N; v++)
			{
				float sum = 0f;
				for (int u = 0; u < N; u++)
				{
					sum += coeffs[u, v] * PrecalcIDCT_X[u, x];
				}
				temp[x, v] = sum;
			}
		}

		for (int x = 0; x < N; x++)
		{
			for (int y = 0; y < N; y++)
			{
				float sum = 0f;
				for (int v = 0; v < N; v++)
				{
					sum += temp[x, v] * PrecalcIDCT_X[v, y];
				}
				output[x, y] = sum + shift;
			}
		}
	}
}