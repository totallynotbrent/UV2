using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ClHcaSharp;

namespace UV2.Live
{
    // one wave entry spliced out of an awb container.
    public class awb_wave
    {
        public int wave_id;
        public long offset;
        public long length;
    }

    // reads afs2 (.awb) wave banks and decodes the hca inside into a playable unity clip.
    public static class live_audio
    {
        // hca key constant from the game client, mixed per wave bank with the awb subkey.
        private const ulong base_key = 75923756697503UL;

        // parses the afs2 header; returns the wave entries.
        public static List<awb_wave> parse_afs2(byte[] bank)
        {
            var waves = new List<awb_wave>();
            if (bank.Length < 0x14 || bank[0] != 'A' || bank[1] != 'F' || bank[2] != 'S' || bank[3] != '2')
                return waves;
            int offset_size = bank[5];
            int waveid_align = BitConverter.ToUInt16(bank, 6);
            int total = BitConverter.ToInt32(bank, 8);
            int offset_align = BitConverter.ToUInt16(bank, 0x0c);
            ushort subkey = BitConverter.ToUInt16(bank, 0x0e);

            int pos = 0x10;
            for (int i = 0; i < total; i++)
            {
                int wave_id = waveid_align == 2
                    ? BitConverter.ToUInt16(bank, pos + i * waveid_align)
                    : BitConverter.ToInt32(bank, pos + i * waveid_align);
                waves.Add(new awb_wave { wave_id = wave_id });
            }
            pos += total * waveid_align;

            for (int i = 0; i < total; i++)
            {
                long start = read_offset(bank, pos, i, offset_size);
                long end = read_offset(bank, pos, i + 1, offset_size);
                if (start % offset_align > 0) start += offset_align - start % offset_align;
                if (end % offset_align > 0 && end < bank.Length) end += offset_align - end % offset_align;
                waves[i].offset = start;
                waves[i].length = end - start;
            }
            return waves;
        }

        private static long read_offset(byte[] bank, int base_pos, int index, int size)
        {
            int p = base_pos + index * size;
            return size == 4 ? BitConverter.ToUInt32(bank, p) : BitConverter.ToUInt16(bank, p);
        }

        // mixes the wave bank's subkey into the client base key.
        public static ulong derive_key(ushort subkey)
        {
            ulong mix = ((ulong)subkey << 16) | (ulong)((ushort)~subkey + 2u);
            unchecked { return base_key * mix; }
        }

        // decodes one hca wave into a unity clip; null on failure.
        // pure decode (no unity api): the sample loop is threadable so the
        // mixer can decode off the main thread and avoid load-phase hitches.
        public static (float[] pcm, int channels, int rate) decode_wave_pcm(byte[] bank, awb_wave wave)
        {
            ushort subkey = BitConverter.ToUInt16(bank, 0x0e);
            ulong key = derive_key(subkey);

            var hca = new byte[wave.length];
            Array.Copy(bank, wave.offset, hca, 0, (int)wave.length);

            using var ms = new MemoryStream(hca);
            var dec = new HcaDecoder(ms, key);
            var info = dec.HcaInfo;
            int channels = info.ChannelCount;
            int spb = info.SamplesPerBlock;

            int delay_blocks = info.EncoderDelay / spb;
            for (int b = 0; b < delay_blocks; b++)
            {
                var skip = new byte[info.BlockSize];
                int got = 0;
                while (got < info.BlockSize)
                {
                    int n = ms.Read(skip, got, info.BlockSize - got);
                    if (n <= 0) break;
                    got += n;
                }
            }

            int total_samples = (info.BlockCount - delay_blocks) * spb - info.EncoderDelay % spb;
            var pcm = new float[total_samples * channels];
            var block = new byte[info.BlockSize];
            var chans = new short[channels][];
            for (int c = 0; c < channels; c++) chans[c] = new short[spb];

            int written = 0;
            for (int b = delay_blocks; b < info.BlockCount; b++)
            {
                int got = 0;
                while (got < info.BlockSize)
                {
                    int n = ms.Read(block, got, info.BlockSize - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < info.BlockSize) break;
                dec.DecodeBlock(block);
                dec.ReadSamples16(chans);
                for (int s = 0; s < spb; s++)
                    for (int c = 0; c < channels; c++)
                    {
                        if (written < pcm.Length)
                            pcm[written++] = chans[c][s] / 32768f;
                    }
            }
            return (pcm, channels, info.SamplingRate);
        }

        public static AudioClip decode_wave(byte[] bank, awb_wave wave, string clip_name)
        {
            ushort subkey = BitConverter.ToUInt16(bank, 0x0e);
            ulong key = derive_key(subkey);

            var hca = new byte[wave.length];
            Array.Copy(bank, wave.offset, hca, 0, (int)wave.length);

            try
            {
                using var ms = new MemoryStream(hca);
                var dec = new HcaDecoder(ms, key);
                var info = dec.HcaInfo;
                int channels = info.ChannelCount;
                int spb = info.SamplesPerBlock;

                // skip the encoder delay so playback starts at sample zero.
                int delay_blocks = info.EncoderDelay / spb;
                for (int b = 0; b < delay_blocks; b++)
                {
                    var skip = new byte[info.BlockSize];
                    int got = 0;
                    while (got < info.BlockSize)
                    {
                        int n = ms.Read(skip, got, info.BlockSize - got);
                        if (n <= 0) break;
                        got += n;
                    }
                }

                int total_samples = (info.BlockCount - delay_blocks) * spb - info.EncoderDelay % spb;
                var pcm = new float[total_samples * channels];
                var block = new byte[info.BlockSize];
                var chans = new short[channels][];
                for (int c = 0; c < channels; c++) chans[c] = new short[spb];

                int written = 0;
                for (int b = delay_blocks; b < info.BlockCount; b++)
                {
                    int got = 0;
                    while (got < info.BlockSize)
                    {
                        int n = ms.Read(block, got, info.BlockSize - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got < info.BlockSize) break;
                    dec.DecodeBlock(block);
                    dec.ReadSamples16(chans);
                    for (int s = 0; s < spb; s++)
                        for (int c = 0; c < channels; c++)
                        {
                            if (written < pcm.Length)
                                pcm[written++] = chans[c][s] / 32768f;
                        }
                }

                var clip = AudioClip.Create(clip_name, total_samples, channels, info.SamplingRate, false);
                clip.SetData(pcm, 0);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[live_audio] hca decode failed for {clip_name}: {e.Message}");
                return null;
            }
        }
    }
}
