using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JPEG.Utilities;

namespace JPEG;

class HuffmanCodec
{
	public static byte[] Encode(IEnumerable<byte> data, out Dictionary<BitsWithLength, byte> decodeTable,
		out long bitsCount)
	{
		var dataArr = data.ToArray();
		var frequences = CalcFrequences(dataArr);

		var root = BuildHuffmanTree(frequences);

		var encodeTable = new BitsWithLength[byte.MaxValue + 1];
		FillEncodeTable(root, encodeTable);

		var bitsBuffer = new BitsBuffer();
		for (var index = 0; index < dataArr.Length; index++)
		{
			var b = dataArr[index];
			bitsBuffer.Add(encodeTable[b].Bits, encodeTable[b].BitsCount);
		}

		decodeTable = CreateDecodeTable(encodeTable);

		return bitsBuffer.ToArray(out bitsCount);
	}

	public static byte[] Decode(byte[] encodedData, Dictionary<BitsWithLength, byte> decodeTable, long bitsCount)
	{
		int maxBitsCount = 16; 
		short[] fastDecodeTable = new short[1 << (maxBitsCount + 1)];
		Array.Fill(fastDecodeTable, (short)-1);

		foreach (var kvp in decodeTable)
		{
			int key = (1 << kvp.Key.BitsCount) | kvp.Key.Bits;
			fastDecodeTable[key] = kvp.Value;
		}
		var result = new List<byte>();
		int currentBits = 0;
		int currentBitsCount = 0;

		for (var byteNum = 0; byteNum < encodedData.Length; byteNum++)
		{
			var b = encodedData[byteNum];
			for (var bitNum = 0; bitNum < 8 && byteNum * 8 + bitNum < bitsCount; bitNum++)
			{
				currentBits = (currentBits << 1) | ((b >> (7 - bitNum)) & 1);
				currentBitsCount++;

				int lookupKey = (1 << currentBitsCount) | currentBits;

				short decodedByte = fastDecodeTable[lookupKey];
        
				if (decodedByte != -1)
				{
					result.Add((byte)decodedByte);
					currentBits = 0;
					currentBitsCount = 0;
				}
			}
		}

		return result.ToArray();
	}

	private static Dictionary<BitsWithLength, byte> CreateDecodeTable(BitsWithLength[] encodeTable)
	{
		var result = new Dictionary<BitsWithLength, byte>(new BitsWithLength.Comparer());
		for (int b = 0; b < encodeTable.Length; b++)
		{
			var bitsWithLength = encodeTable[b];
			if (bitsWithLength == null)
				continue;

			result[bitsWithLength] = (byte)b;
		}

		return result;
	}

	private static void FillEncodeTable(HuffmanNode node, BitsWithLength[] encodeSubstitutionTable,
		int bitvector = 0, int depth = 0)
	{
		if (node.LeafLabel != null)
			encodeSubstitutionTable[node.LeafLabel.Value] =
				new BitsWithLength { Bits = bitvector, BitsCount = depth };
		else
		{
			if (node.Left != null)
			{
				FillEncodeTable(node.Left, encodeSubstitutionTable, (bitvector << 1) + 1, depth + 1);
				FillEncodeTable(node.Right, encodeSubstitutionTable, (bitvector << 1) + 0, depth + 1);
			}
		}
	}
	// private static HuffmanNode BuildHuffmanTree(int[] frequences)
	// {
	// 	var nodes = GetNodes(frequences);
	//
	// 	while (nodes.Count() > 1)
	// 	{
	// 		var firstMin = nodes.MinOrDefault(node => node.Frequency);
	// 		nodes = nodes.Without(firstMin);
	// 		var secondMin = nodes.MinOrDefault(node => node.Frequency);
	// 		nodes = nodes.Without(secondMin);
	// 		nodes = nodes.Concat(new HuffmanNode
	// 				{ Frequency = firstMin.Frequency + secondMin.Frequency, Left = secondMin, Right = firstMin }
	// 			.ToEnumerable());
	// 	}
	//
	// 	return nodes.First();
	// }
	
	private static HuffmanNode BuildHuffmanTree(int[] frequences)
	{
		var nodes = GetNodes(frequences);

		while (nodes.Count > 1)
		{
			var firstMin = nodes.FirstOrDefault();
			var secondMin = nodes.Skip(1).FirstOrDefault();
			if (nodes.Count != 0)
				nodes.Remove(firstMin);
			if (nodes.Count != 0)
				nodes.Remove(secondMin);
			var newNode = new HuffmanNode
			{
				Frequency = 0,
				Right = firstMin,
				Left = secondMin
			};
			if (firstMin != null)
			{
				newNode.Frequency += firstMin.Frequency;
			}
			if (secondMin != null)
			{
				newNode.Frequency += secondMin.Frequency;
			}
			nodes.Add(newNode);
		}

		return nodes.First();
	}
	
	

	private static List<HuffmanNode> GetNodes(int[] frequences)
	{
		return Enumerable.Range(0, byte.MaxValue + 1)
			.Select(num => new HuffmanNode { Frequency = frequences[num], LeafLabel = (byte)num })
			.Where(node => node.Frequency > 0)
			.OrderBy(node => node.Frequency)
			.ToList();
	}
	
	private static int[] CalcFrequences(byte[] data) 
	{
		var result = new int[256];
		
		for (int i = 0; i < data.Length; i++)
		{
			result[data[i]]++;
		}
    
		return result;
	}
}