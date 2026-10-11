using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SpaceBattle.Game;

internal static class PngCapture
{
    public static void Save(string path, int width, int height, byte[] rgba)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output=File.Create(path); output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string name, byte[] bytes)
        {
            Span<byte> n=stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(n,bytes.Length); output.Write(n);
            var tag=Encoding.ASCII.GetBytes(name); output.Write(tag); output.Write(bytes);
            uint crc=uint.MaxValue;
            foreach(byte b in tag.Concat(bytes)) { crc^=b; for(int k=0;k<8;k++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u); }
            BinaryPrimitives.WriteUInt32BigEndian(n,~crc); output.Write(n);
        }
        var header=new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header,width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height); header[8]=8;header[9]=6;Chunk("IHDR",header);
        using var compressed=new MemoryStream();
        using(var z=new ZLibStream(compressed,CompressionLevel.Fastest,true)) for(int y=height-1;y>=0;y--) { z.WriteByte(0); z.Write(rgba,y*width*4,width*4); }
        Chunk("IDAT",compressed.ToArray()); Chunk("IEND",[]);
    }
}
