using UnityEngine;

namespace PockHammer.Audio
{
    /// <summary>
    /// 程序化音效合成器：所有音效都是启动时用代码算出来的，不依赖任何 wav 资源。
    /// 好处是包体不增加、不用担心素材版权；坏处是音色偏电子，
    /// 后面拿到正式音源，只要把 <see cref="GameSfx"/> 里换成 AudioClip 资源即可，调用方一行都不用改。
    ///
    /// 每个音效都是单声道，生成后统一归一化到峰值 0.85，响度靠播放时的 volume 控制。
    /// </summary>
    public static class SfxSynth
    {
        public const int SampleRate = 44100;

        // ------------------------------ 对外：生成各种音效 ------------------------------

        /// <summary>挥空的呼呼声（近战挥锤）。</summary>
        public static AudioClip Swing()
        {
            return ToClip("sfx_swing", WhooshData(0.30f, 420f, 1900f, 2.4f, 0.28f, 11));
        }

        /// <summary>砸中东西的闷响（南瓜 / 动物通用）。</summary>
        public static AudioClip Hit()
        {
            return ToClip("sfx_hit", ImpactData(210f, 105f, 0.32f, 0.10f, 1100f, 17));
        }

        /// <summary>砸中时更小更脆的一下（接住锤子之类的轻碰撞，复用同一套算法改参数）。</summary>
        public static AudioClip Catch()
        {
            return ToClip("sfx_catch", ImpactData(380f, 210f, 0.16f, 0.045f, 2000f, 23));
        }

        /// <summary>锤爆：低频爆开 + 黏糊糊的撕裂 + 几粒碎块飞出。</summary>
        public static AudioClip Burst()
        {
            return ToClip("sfx_burst", BurstData(0.9f, 31));
        }

        /// <summary>雷神之锤脱手：起手一记低频，然后快速上扬的风声。</summary>
        public static AudioClip Throw()
        {
            return ToClip("sfx_throw", ThrowData());
        }

        /// <summary>锤子往回飞：越来越近、音高上扬的嗡鸣。</summary>
        public static AudioClip ReturnWhirr()
        {
            return ToClip("sfx_return", ReturnData());
        }

        /// <summary>冷却回满的两声提示音（叮—叮）。</summary>
        public static AudioClip Ready()
        {
            return ToClip("sfx_ready", ReadyData());
        }

        /// <summary>冷却没好却硬要扔：一声短促的"不行"。</summary>
        public static AudioClip Denied()
        {
            return ToClip("sfx_denied", DeniedData());
        }

        /// <summary>农场环境风声，可无缝循环。</summary>
        public static AudioClip Wind()
        {
            return ToClip("sfx_wind", WindData(4f, 7));
        }

        // ------------------------------ 合成核心 ------------------------------

        /// <summary>带通式风声：中心频率从 startHz 扫到 endHz，包络两头收，听起来像"呼——"。</summary>
        private static float[] WhooshData(float seconds, float startHz, float endHz, float q, float bodyMix, int seed)
        {
            int length = Mathf.RoundToInt(seconds * SampleRate);
            var data = new float[length];
            var rng = new System.Random(seed);
            var band = new BandPass();
            band.Init(startHz, q);
            var low = new OnePole();

            for (int i = 0; i < length; i++)
            {
                float u = (float)i / length;
                float shaped = u * u * (3f - 2f * u);       // 平滑的加速上扬
                float noise = NextNoise(rng);

                band.Modulate(Mathf.Lerp(startHz, endHz, shaped), q);
                float banded = band.Process(noise);
                float body = low.Process(noise, Mathf.Lerp(startHz * 0.5f, endHz * 0.35f, shaped));

                float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 1.35f);
                data[i] = (banded * 1.7f + body * bodyMix) * envelope;
            }

            return data;
        }

        /// <summary>撞击：低频"咚" + 极短的噪声冲击 + 一点中频脆响。</summary>
        private static float[] ImpactData(float lowStartHz, float lowEndHz, float seconds, float decay, float crackHz, int seed)
        {
            int length = Mathf.RoundToInt(seconds * SampleRate);
            var data = new float[length];
            var rng = new System.Random(seed);
            var crack = new BandPass();
            crack.Init(crackHz, 1.3f);
            var lowPass = new OnePole();
            double phase = 0.0;

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float noise = NextNoise(rng);

                // 音高快速下滑，是"咚"而不是"叮"
                float pitchT = Mathf.Clamp01(t / 0.10f);
                float freq = Mathf.Lerp(lowStartHz, lowEndHz, Mathf.Sqrt(pitchT));
                phase += 2.0 * Mathf.PI * freq / SampleRate;

                float body = Mathf.Sin((float)phase) * Mathf.Exp(-t / decay);

                // 三次谐波 + 40ms 快衰减 = 木槌砸到东西那一下"啪"的实体感。
                // 头显的小喇叭放不出几十赫兹，全靠这段中频把声音"顶"出来。
                float thock = Mathf.Sin((float)phase * 3f) * Mathf.Exp(-t / 0.040f);

                float transientNoise = lowPass.Process(noise, 3200f) * Mathf.Exp(-t / 0.018f);
                float crackPart = crack.Process(noise) * Mathf.Exp(-t / 0.05f);

                data[i] = body * 0.42f + thock * 1.05f + transientNoise * 0.85f + crackPart * 0.95f;
            }

            return data;
        }

        /// <summary>爆开：低频冲击 + 从高往低扫的黏稠噪声 + 几粒碎块飞出的颗粒声。</summary>
        private static float[] BurstData(float seconds, int seed)
        {
            int length = Mathf.RoundToInt(seconds * SampleRate);
            var data = new float[length];
            var rng = new System.Random(seed);
            var splat = new BandPass();
            splat.Init(2600f, 0.9f);
            var lowPass = new OnePole();
            double phase = 0.0;

            // 碎块颗粒：随机几个时间点，每个几十毫秒
            var grainStart = new float[7];
            var grainLength = new float[7];
            for (int g = 0; g < grainStart.Length; g++)
            {
                grainStart[g] = 0.06f + (float)rng.NextDouble() * 0.55f;
                grainLength[g] = 0.03f + (float)rng.NextDouble() * 0.05f;
            }

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float noise = NextNoise(rng);

                // 1) 低频轰的一下（频段抬高到 200~80，太低的头显放不出来）
                float freq = Mathf.Lerp(200f, 80f, Mathf.Clamp01(t / 0.18f));
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float boom = Mathf.Sin((float)phase) * Mathf.Exp(-t / 0.22f);

                // 2) 啪叽：中心频率快速下坠，带一点点起始的爆点
                splat.Modulate(Mathf.Lerp(2800f, 420f, Mathf.Clamp01(t / 0.45f)), 0.9f);
                float splatEnv = (1f - Mathf.Exp(-t / 0.006f)) * Mathf.Exp(-t / 0.17f);
                float splatPart = splat.Process(noise) * splatEnv;

                // 3) 一层中频撕裂，保证在头显上听起来是"炸开"而不是"噗"一声
                float tear = lowPass.Process(noise, 1400f) * Mathf.Exp(-t / 0.10f);

                // 3) 碎块
                float grains = 0f;
                for (int g = 0; g < grainStart.Length; g++)
                {
                    float local = t - grainStart[g];
                    if (local < 0f || local > grainLength[g])
                    {
                        continue;
                    }

                    float window = Mathf.Sin(Mathf.PI * local / grainLength[g]);
                    grains += lowPass.Process(noise, 4500f) * window;
                }

                data[i] = boom * 0.7f + splatPart * 1.45f + tear * 1.65f + grains * 0.28f;
            }

            return data;
        }

        /// <summary>扔出去：一记低频"轰"接一段快速上扬的风声。</summary>
        private static float[] ThrowData()
        {
            float[] whoosh = WhooshData(0.55f, 300f, 2700f, 2.0f, 0.35f, 41);
            int length = whoosh.Length;
            var data = new float[length];
            double phase = 0.0;

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float freq = Mathf.Lerp(105f, 42f, Mathf.Clamp01(t / 0.22f));
                phase += 2.0 * Mathf.PI * freq / SampleRate;
                float thump = Mathf.Sin((float)phase) * Mathf.Exp(-t / 0.13f);
                data[i] = whoosh[i] + thump * 0.8f;
            }

            return data;
        }

        /// <summary>回旋飞回：中心频率上扬 + 一个带颤音的机械嗡鸣。</summary>
        private static float[] ReturnData()
        {
            float[] whoosh = WhooshData(0.62f, 380f, 2100f, 2.8f, 0.2f, 53);
            int length = whoosh.Length;
            var data = new float[length];
            double phase = 0.0;

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float u = (float)i / length;
                float freq = Mathf.Lerp(170f, 760f, u * u);
                float vibrato = 1f + 0.02f * Mathf.Sin(t * 46f);
                phase += 2.0 * Mathf.PI * freq * vibrato / SampleRate;

                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 1.1f);
                data[i] = whoosh[i] + Mathf.Sin((float)phase) * env * 0.22f;
            }

            return data;
        }

        /// <summary>冷却回满：两声清亮的上行提示音。</summary>
        private static float[] ReadyData()
        {
            int length = Mathf.RoundToInt(0.9f * SampleRate);
            var data = new float[length];
            AddNote(data, 880f, 0.00f, 0.60f, 0.55f, 0.30f);
            AddNote(data, 1318.5f, 0.10f, 0.75f, 0.50f, 0.26f);
            return data;
        }

        /// <summary>还不能扔：一声短促闷响，不刺耳但明确。</summary>
        private static float[] DeniedData()
        {
            int length = Mathf.RoundToInt(0.16f * SampleRate);
            var data = new float[length];
            var lowPass = new OnePole();

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-t / 0.055f);
                // 方波味的低频，比正弦更"堵"
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 320f * t));
                data[i] = lowPass.Process(square, 1400f) * env;
            }

            return data;
        }

        /// <summary>风：低通噪声 + 缓慢起伏的音量，首尾交叉淡化做成无缝循环。</summary>
        private static float[] WindData(float seconds, int seed)
        {
            int length = Mathf.RoundToInt(seconds * SampleRate);
            int fade = Mathf.RoundToInt(0.35f * SampleRate);
            var raw = new float[length + fade];
            var rng = new System.Random(seed);
            var lp1 = new OnePole();
            var lp2 = new OnePole();

            for (int i = 0; i < raw.Length; i++)
            {
                float t = (float)i / SampleRate;
                float noise = NextNoise(rng);
                float cutoff = 420f + Mathf.Sin(t * 0.9f) * 130f;
                float filtered = lp2.Process(lp1.Process(noise, cutoff), cutoff);
                float mod = 0.62f + 0.38f * Mathf.Sin(t * 1.7f + 0.6f) * Mathf.Sin(t * 0.63f);
                raw[i] = filtered * mod;
            }

            for (int i = 0; i < fade; i++)
            {
                float s = (float)i / fade;
                raw[i] = raw[i] * s + raw[length + i] * (1f - s);
            }

            var data = new float[length];
            System.Array.Copy(raw, data, length);
            return data;
        }

        /// <summary>往缓冲区里叠一个带泛音的钟琴音，用于提示音。</summary>
        private static void AddNote(float[] data, float freq, float startSeconds, float seconds, float gain, float decay)
        {
            int start = Mathf.RoundToInt(startSeconds * SampleRate);
            int count = Mathf.RoundToInt(seconds * SampleRate);

            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float env = (1f - Mathf.Exp(-t / 0.005f)) * Mathf.Exp(-t / decay);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t)
                           + 0.35f * Mathf.Sin(4f * Mathf.PI * freq * t)
                           + 0.12f * Mathf.Sin(6f * Mathf.PI * freq * t);
                data[start + i] += tone * env * gain;
            }
        }

        // ------------------------------ 小工具 ------------------------------

        private static float NextNoise(System.Random rng)
        {
            return (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        private static AudioClip ToClip(string name, float[] data)
        {
            Normalize(data, 0.85f);

            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void Normalize(float[] data, float peak)
        {
            float max = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float value = Mathf.Abs(data[i]);
                if (value > max)
                {
                    max = value;
                }
            }

            if (max < 0.00001f)
            {
                return;
            }

            float scale = peak / max;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= scale;
            }
        }

        /// <summary>一阶低通，用来把白噪声磨成"空气感"。</summary>
        private struct OnePole
        {
            private float _state;

            public float Process(float input, float cutoffHz)
            {
                float a = Mathf.Exp(-2f * Mathf.PI * Mathf.Clamp(cutoffHz, 20f, SampleRate * 0.45f) / SampleRate);
                _state = (1f - a) * input + a * _state;
                return _state;
            }
        }

        /// <summary>
        /// 状态变量滤波器（只取带通那一路），中心频率可以逐样本扫动，
        /// 这是"呼——"这种风声的关键：噪声不动、滤波器在动。
        /// </summary>
        private struct BandPass
        {
            private float _low;
            private float _band;
            private float _f;
            private float _damping;

            public void Init(float cutoffHz, float q)
            {
                _low = 0f;
                _band = 0f;
                _damping = 1f / Mathf.Max(0.6f, q);
                Modulate(cutoffHz, q);
            }

            public void Modulate(float cutoffHz, float q)
            {
                _damping = 1f / Mathf.Max(0.6f, q);
                _f = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(cutoffHz, 20f, 6000f) / SampleRate);
                _f = Mathf.Clamp(_f, 0.0001f, 0.9f);
            }

            public float Process(float input)
            {
                _low += _f * _band;
                float high = input - _low - _damping * _band;
                _band += _f * high;
                return _band;
            }
        }
    }
}