namespace JPEG;

class HuffmanNode
{
    public byte? LeafLabel { get; set; }
    public int Frequency { get; set; }
    public HuffmanNode Left { get; set; }
    public HuffmanNode Right { get; set; }
}