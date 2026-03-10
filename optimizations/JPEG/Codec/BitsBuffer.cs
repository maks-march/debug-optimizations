using System.Collections.Generic;

namespace JPEG;

class BitsBuffer
{
    private readonly List<byte> _buffer = new List<byte>(1500000); 
    private int _accumulator = 0;
    private int _bitCount = 0;

    public void Add(int bits, int length)
    {
        _accumulator = (_accumulator << length) | bits;
        _bitCount += length;

        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            
            _buffer.Add((byte)(_accumulator >> _bitCount));
        }
    }

    public byte[] ToArray(out long bitsCount)
    {
        bitsCount = _buffer.Count * 8L;
        _buffer.Add((byte)(_accumulator << 8 - _bitCount));
        _bitCount = 0;
        _accumulator = 0;
        return _buffer.ToArray();
    }
}