using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kuriimu.IO;

namespace text_metal
{
    public sealed class ARRNAM
    {
        public List<Entry> Entries = new List<Entry>();

        public ARRNAM(Stream namInput, Stream arrInput)
        {
            List<ArrEntry> arrEntries;

            using (var br = new BinaryReaderX(arrInput, false))
            {
                arrEntries = br.ReadMultiple<ArrEntry>((int)br.BaseStream.Length / 12);
            }

            using (var br = new BinaryReaderX(namInput, false))
            {
                var lastOffset = -1;
                
                for (int i = 0; i < arrEntries.Count; i++)
                {
                    var arrEntry = arrEntries[i];
                    br.BaseStream.Position = arrEntry.Offset;

                    var bad = false;
                    var chars = br.ReadBytes(2);
                    var text = Encoding.Unicode.GetString(chars);
                    while (!chars.SequenceEqual(new byte[] { 0, 0 }))
                    {
                        chars = br.ReadBytes(2);
                        text += Encoding.Unicode.GetString(chars);

                        if (chars.SequenceEqual(new byte[] { 0, 0 }))
                            break;
                    }

                    if (arrEntry.Offset > lastOffset)
                        lastOffset = arrEntry.Offset;
                    else
                        bad = true;

                    var entry = new Entry
                    {
                        ArrEntry = arrEntry,
                        Index = i + 1 + (bad ? 1000 : 0),
                        Text = text.TrimEnd('\0')
                    };

                    Entries.Add(entry);
                }
            }
        }

        public void Save(Stream namOutput, Stream arrOutput)
        {
            var added = new Dictionary<string, short>();

            using (var bw = new BinaryWriterX(namOutput))
                foreach (var entry in Entries)
                {
                    var arrEntry = entry.ArrEntry;

                    if (!added.ContainsKey(entry.Text))
                    {
                        arrEntry.Offset = (short)bw.BaseStream.Position;
                        added.Add(entry.Text, arrEntry.Offset);
                        bw.Write(Encoding.Unicode.GetBytes(entry.Text));
                        bw.Write(new byte[] { 0, 0 });
                    }
                    else
                        arrEntry.Offset = added[entry.Text];
                }

            using (var bw = new BinaryWriterX(arrOutput))
                foreach (var entry in Entries)
                    bw.WriteStruct(entry.ArrEntry);
        }
    }
}