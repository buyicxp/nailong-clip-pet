using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace NailongClipPet
{
    /// <summary>
    /// 程序内置音效合成器：不依赖外部素材，首次运行时在 Assets\sounds 下
    /// 生成 6 个短 wav，之后直接播放。
    /// </summary>
    public static class Audio
    {
        const int SR = 44100;
        static string _dir;
        static readonly Dictionary<string, SoundPlayer> _players = new Dictionary<string, SoundPlayer>();
        public static bool Enabled = true;

        public static void Init(string assetsDir)
        {
            try
            {
                _dir = Path.Combine(assetsDir, "sounds");
                Directory.CreateDirectory(_dir);

                Ensure("copy_pop", MakeCopyPop);
                Ensure("cut_swallow", MakeSwallow);
                Ensure("paste_thud", MakeThud);
                Ensure("cut_spit", MakeSpit);
                Ensure("cancel_whoosh", MakeWhoosh);
                Ensure("belly_rumble", MakeRumble);

                foreach (var kv in _players) { }
            }
            catch { }
        }

        static void Ensure(string name, Func<float[]> gen)
        {
            string path = Path.Combine(_dir, name + ".wav");
            if (!File.Exists(path))
            {
                float[] samples = gen();
                WriteWav(path, samples);
            }
            var sp = new SoundPlayer(path);
            try { sp.Load(); } catch { }
            _players[name] = sp;
        }

        public static void Play(string name)
        {
            if (!Enabled) return;
            var sp = _players.ContainsKey(name) ? _players[name] : null;
            if (sp == null) return;
            try { sp.Play(); } catch { }
        }

        // ---------------- 合成器 ----------------

        static float[] MakeCopyPop()
        {
            int n = (int)(SR * 0.09);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 34.0);
                double f = 620 + 260 * Math.Min(1.0, t / 0.03);
                buf[i] = (float)(Math.Sin(2 * Math.PI * f * t) * env * 0.85);
            }
            return buf;
        }

        static float[] MakeSwallow()
        {
            int n = (int)(SR * 0.42);
            var buf = new float[n];
            double lp = 0;
            var rnd = new Random(7);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double p = t / 0.42;
                double f = 560 - 380 * p + 90 * Math.Sin(p * Math.PI * 4);
                double body = Math.Sin(2 * Math.PI * f * t) * 0.55;
                double noise = (rnd.NextDouble() * 2 - 1) * 0.35;
                lp = lp * 0.85 + noise * 0.15;
                double env = Math.Sin(Math.PI * Math.Min(1.0, p * 1.15)) * Math.Exp(-p * 1.4);
                double gulp = Math.Max(0, 1 - Math.Abs(p - 0.72) * 7.0);
                buf[i] = (float)((body + lp) * env * (0.6 + gulp * 0.9));
            }
            return buf;
        }

        static float[] MakeThud()
        {
            int n = (int)(SR * 0.13);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double env = Math.Exp(-t * 26.0);
                buf[i] = (float)((Math.Sin(2 * Math.PI * 132 * t) * 0.7 +
                    Math.Sin(2 * Math.PI * 96 * t) * 0.4) * env);
            }
            return buf;
        }

        static float[] MakeSpit()
        {
            int n = (int)(SR * 0.30);
            var buf = new float[n];
            var rnd = new Random(11);
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double p = t / 0.30;
                double env = Math.Exp(-p * 3.2) * (p < 0.06 ? p / 0.06 : 1.0);
                double noise = (rnd.NextDouble() * 2 - 1);
                lp = lp * 0.72 + noise * 0.28;
                double poof = Math.Sin(2 * Math.PI * (210 - 150 * p) * t) * 0.35;
                buf[i] = (float)((lp * 0.55 + poof) * env);
            }
            return buf;
        }

        static float[] MakeWhoosh()
        {
            int n = (int)(SR * 0.34);
            var buf = new float[n];
            var rnd = new Random(3);
            double lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                double p = i / (double)n;
                double noise = rnd.NextDouble() * 2 - 1;
                lp = lp * 0.9 + noise * 0.1;
                lp2 = lp2 * 0.96 + lp * 0.04;
                double env = Math.Pow(1 - p, 2.0);
                buf[i] = (float)(lp2 * env * 1.6);
            }
            return buf;
        }

        static float[] MakeRumble()
        {
            int n = (int)(SR * 0.9);
            var buf = new float[n];
            var rnd = new Random(23);
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SR;
                double p = t / 0.9;
                double wob = 0.55 + 0.45 * Math.Sin(2 * Math.PI * 2.6 * t);
                double noise = rnd.NextDouble() * 2 - 1;
                lp = lp * 0.94 + noise * 0.06;
                buf[i] = (float)((Math.Sin(2 * Math.PI * 88 * t) * 0.35 + lp * 0.9) * wob * (1 - p) * 0.7);
            }
            return buf;
        }

        static void WriteWav(string path, float[] samples)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                int dataLen = samples.Length * 2;
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataLen);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);      // PCM
                bw.Write((short)1);      // mono
                bw.Write(SR);
                bw.Write(SR * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(dataLen);
                foreach (float s in samples)
                {
                    double v = Math.Max(-1.0, Math.Min(1.0, s));
                    bw.Write((short)(v * 32000));
                }
            }
        }
    }
}
