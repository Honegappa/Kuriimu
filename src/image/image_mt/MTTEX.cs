using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Cetera.Image;
using Kuriimu.IO;

namespace image_mt
{
    class MTTEX
    {
        public List<Bitmap> Bitmaps = new List<Bitmap>();
        private const int MinHeight = 8;

        private Header Header;
        public HeaderInfo HeaderInfo { get; set; }
        private ImageSettings Settings = new ImageSettings();
        private ByteOrder ByteOrder = ByteOrder.LittleEndian;

        public MTTEX(Stream input)
        {
            using (var br = new BinaryReaderX(input))
            {
                // Set endianess
                if (br.PeekString() == "\0XET")
                    br.ByteOrder = ByteOrder = ByteOrder.BigEndian;

                // Header
                Header = br.ReadStruct<Header>();
                HeaderInfo = new HeaderInfo
                {
                    // Block 1
                    Version = (int)(Header.Block1 & 0xFFF),
                    Unknown1 = (int)((Header.Block1 >> 12) & 0xFFF),
                    Unused1 = (int)((Header.Block1 >> 24) & 0xF),
                    AlphaChannelFlags = (AlphaChannelFlags)((Header.Block1 >> 28) & 0xF),
                    // Block 2
                    MipMapCount = (int)(Header.Block2 & 0x3F),
                    Width = (int)((Header.Block2 >> 6) & 0x1FFF),
                    Height = (int)((Header.Block2 >> 19) & 0x1FFF),
                    // Block 3
                    Unknown2 = (int)(Header.Block3 & 0xFF),
                    Format = (Format)((Header.Block3 >> 8) & 0xFF),
                    Unknown3 = (int)((Header.Block3 >> 16) & 0xFFFF)
                };

                var mipMaps = br.ReadMultiple<int>(HeaderInfo.MipMapCount);
                Settings.Format = ImageSettings.ConvertFormat(HeaderInfo.Format);
                if (Settings.Format == Cetera.Image.Format.DXT5)
                {
                    Settings.ZOrder = false;
                    Settings.TileSize = 4;
                }

                for (var i = 0; i < mipMaps.Count; i++)
                {
                    var texDataSize = (i + 1 < mipMaps.Count ? mipMaps[i + 1] : (int)br.BaseStream.Length) - mipMaps[i];
                    Settings.Width = HeaderInfo.Width >> i;
                    Settings.Height = Math.Max(HeaderInfo.Height >> i, MinHeight);

                    if (Settings.Format == Cetera.Image.Format.DXT1 || Settings.Format == Cetera.Image.Format.DXT5 && HeaderInfo.AlphaChannelFlags == AlphaChannelFlags.YCbCrTransform)
                        Bitmaps.Add(CapcomTransform(Common.Load(br.ReadBytes(texDataSize), Settings), TransformDirection.ToProperColors));
                    else
                        Bitmaps.Add(Common.Load(br.ReadBytes(texDataSize), Settings));
                }
            }
        }

        private Bitmap CapcomTransform(Bitmap orig, TransformDirection direction)
        {
            // currently trying out YCbCr:
            // https://en.wikipedia.org/wiki/YCbCr#JPEG_conversion
            var attr = new ImageAttributes();
            switch (direction)
            {
                case TransformDirection.ToProperColors:
                    attr.SetColorMatrix(new ColorMatrix(new[] {
                        new[] { 1.402f,-0.71414f, 0,      0, 0f },
                        new[] { 0,      0,        0,      1, 0f },
                        new[] { 0,     -0.34414f, 1.772f, 0, 0f },
                        new[] { 1,      1,        1,      0, 0f },
                        new[] {-0.676f, 0.51046f,-0.855f, 0, 1f }
                    }));
                    break;
                case TransformDirection.ToOptimizedColors:
                    attr.SetColorMatrix(new ColorMatrix(new[] {
                        new[] { 0.50000f, 0f,-0.16874f, 0.29900f, 0f },
                        new[] {-0.41869f, 0f,-0.33126f, 0.58700f, 0f },
                        new[] {-0.08131f, 0f, 0.50000f, 0.11400f, 0f },
                        new[] { 0,        1,  0,        0,        0f },
                        new[] { 0.54477f, 0f, 0.09778f,-0.08666f, 1f }
                    }));
                    break;
            }

            var transformed = new Bitmap(orig.Width, orig.Height);
            using (var g = Graphics.FromImage(transformed))
                g.DrawImage(orig, new Rectangle(0, 0, orig.Width, orig.Height), 0, 0, orig.Width, orig.Height, GraphicsUnit.Pixel, attr);
            return transformed;
        }

        public void Save(Stream output)
        {
            using (var bw = new BinaryWriterX(output))
            {
                Header.Block1 = (uint)(HeaderInfo.Version | (HeaderInfo.Unknown1 << 12) | (HeaderInfo.Unused1 << 24) | ((int)HeaderInfo.AlphaChannelFlags << 28));
                Header.Block2 = (uint)(HeaderInfo.MipMapCount | (HeaderInfo.Width << 6) | (HeaderInfo.Height << 19));
                Header.Block3 = (uint)(HeaderInfo.Unknown2 | ((int)HeaderInfo.Format << 8) | (HeaderInfo.Unknown3 << 16));
                bw.WriteStruct(Header);

                Settings.Format = ImageSettings.ConvertFormat(HeaderInfo.Format);
                var bitmaps = Bitmaps.Select(bmp => Common.Save(bmp, Settings)).ToList();

                var offset = 0;
                foreach (var bitmap in bitmaps)
                {
                    bw.Write(offset);
                    offset += bitmap.Length;
                }

                foreach (var bitmap in bitmaps)
                    bw.Write(bitmap);
            }
        }
    }
}
