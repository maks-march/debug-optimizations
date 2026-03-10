using BenchmarkDotNet.Running;

namespace Benchmarks;

internal class Program
{
	static void Main(string[] args)
	{
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		//BenchmarkRunner.Run<MemoryTraffic>();
		//BenchmarkRunner.Run<StructVsClassBenchmark>();
		//BenchmarkRunner.Run<ByteArrayEqualityBenchmark>();
		//BenchmarkRunner.Run<NewConstraintBenchmark>();
		//BenchmarkRunner.Run<MaxBenchmark>();
	}
}