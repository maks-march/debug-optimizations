using DotTraceExamples;
using ImageProcessing;
using NUnit.Framework;

namespace DotTraceExamplesTest;

public class ImageProcessingAlgorithmsTest
{
	private RGBImage image;

	[SetUp]
	public void Setup()
	{
		const string fileName = @"TestImages\TestImage.jpg";
		using var fileStream = File.OpenRead(fileName);
		image = RGBImage.FromStream(fileStream);
	}

	[Test]
	public void EdgePreservingSmoothing()
	{
		image.EdgePreservingSmoothing();
	}

	[Test]
	public void MeanShift()
	{
		image.MeanShift();
	}
}