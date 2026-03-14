using BenchmarkDotNet.Attributes;

namespace JPEG.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 50)]
public class IntClampBenchmark
{
    [Benchmark]
    public void ToByte()
    {
        for (int i = -100; i < 1000; i++)
        {
            var a = ToByte(i);
        }
    }
	
    [Benchmark]
    public void Clamp()
    {
        for (int i = -100; i < 1000; i++)
        {
            var a = (byte)Math.Clamp(i, 0, 255);
        }
    }
	
    private static byte ToByte(int x)
    {
        if (x < 0) return 0;
        else if (x > 255) return 255;
        return (byte)x;
    }
    
}