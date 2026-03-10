using System.Numerics;
using DotTraceExamples;
using NUnit.Framework;

namespace DotTraceExamplesTest;

public class ComplexArrayTests
{
	private Complex[] data;
	private Complex[] dividedData;

	[SetUp]
	public void Setup()
	{
		data = new Complex[13000000];
		dividedData = data.DivideByNumber(Math.PI);
	}

	[Test]
	public void DivideByNumber()
	{
		data.DivideByNumber(Math.PI);
	}

	[Test]
	public void SumModules()
	{
		dividedData.SumModules();
	}
}