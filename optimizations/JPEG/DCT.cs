using System;
using JPEG.Utilities;

namespace JPEG;

public class DCT
{
	private const int N = 8;
	private static double _beta = 1d / N + 1d / N;
	private static readonly double[,] cosX = new double[N, N];
	private static readonly double[,] cosY = new double[N, N];

	static DCT()
	{
		BuildCosCache(N);
	}

	private static void BuildCosCache(int N)
	{
		for (int u = 0; u < N; u++)
		for (int x = 0; x < N; x++)
			cosX[u, x] = Math.Cos((2d * x + 1d) * u * Math.PI / (2 * N));

		for (int v = 0; v < N; v++)
		for (int y = 0; y < N; y++)
			cosY[v, y] = Math.Cos((2d * y + 1d) * v * Math.PI / (2 * N));
	}
	
	public static double[,] DCT2D(double[,] input, short shift = -128)
	{
		var coeffs = new double[N, N];

		for (int u = 0; u < N; u++)
		{
			for (int v = 0; v < N; v++)
			{
				double sum = 0.0;
				for (int x = 0; x < N; x++)
				{
					for (int y = 0; y < N; y++)
					{
						sum += (input[x, y] + shift) * cosX[u, x] * cosY[v, y];
					}
				}
				byte flagU = (byte)((8 - u) / 8);
				byte flagV = (byte)((8 - v) / 8);
				coeffs[u, v] = sum * _beta * (flagU * 1 / Math.Sqrt(2) + 1 - flagU) * (flagV * 1 / Math.Sqrt(2) + 1 - flagV);
			}
		}

		return coeffs;
	}

	public static void IDCT2D(double[,] coeffs, double[,] output, short shift = 128)
	{
		for (var x = 0; x < N; x++)
		{
			for (var y = 0; y < N; y++)
			{
				double sum = 0.0;
				for (int u = 0; u < N; u++)
				{
					for (int v = 0; v < N; v++)
					{
						byte flagU = (byte)((8 - u) / 8);
						byte flagV = (byte)((8 - v) / 8);
						sum += coeffs[u, v] * cosX[u, x] * cosY[v, y] *
						       (flagU * 1 / Math.Sqrt(2) + 1 - flagU) * (flagV * 1 / Math.Sqrt(2) + 1 - flagV);
					}
				}

				output[x, y] = sum * _beta + shift;
			}
		}
	}
}