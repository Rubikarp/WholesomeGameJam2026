using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class AnimationsSlide : BasicUsageSlide
    {
        public override string Text =>
            "🎬 <b>Animation modifiers — pure functions of Phase</b>\n\n" +
            "<wave>Wave: riding the sine</wave>\n" +
            "<wobble>Wobble: jelly rocking 😀</wobble>\n" +
            "<pulse>Pulse: scale beat ❤️</pulse>\n" +
            "<bounce>Bounce: jump jump jump!</bounce>\n" +
            "<shake>Shake: EARTHQUAKE WARNING</shake>\n" +
            "<float>Float: weightless drift</float>\n" +
            "<pendulum>Pendulum: hanging sign</pendulum>\n" +
            "<spin>Spin: whirling glyphs</spin>\n" +
            "<glitch>Glitch: SIGNAL_LOST://reconnect</glitch>\n" +
            "<scramble>Scramble: DECRYPTING CLASSIFIED LINE...</scramble>\n" +
            "Roll: <roll>1234567</roll>  <roll=1.5>42195</roll> <size=72%>(spread 1.5: slot-machine)</size>\n" +
            "Flap: <flap>NOW BOARDING</flap> <size=72%>(letter wheel: split-flap board)</size>\n\n" +
            "<size=64%><color=#888>Each renders one exact state of an external Phase: the demo advances it from Update(), but a tween or your code can set or scrub it — Shake and Glitch replay identically backwards. Scramble's Progress and the wheels' Roll are driven from Update(). Shape params ride the tags: \\<wave=8,2,0.25> gives <wave=8,2,0.25>amplitude, frequency, spread</wave>. No active range — no per-frame cost.</color></size>";

        private ScrambleModifier scrambleModifier;
        private RollingModifier rollingModifier;
        private RollingModifier flapModifier;
        private float scrambleTime;
        private float rollTime;

        public override void Register(BasicUsageExampleBase example)
        {
            scrambleModifier = new ScrambleModifier();
            example.AddStyle(scrambleModifier, new TagRule("scramble"));
            rollingModifier = new RollingModifier();
            example.AddStyle(rollingModifier, new TagRule("roll"));
            flapModifier = new RollingModifier { Wheel = "ABCDEFGHIJKLMNOPQRSTUVWXYZ " };
            example.AddStyle(flapModifier, new TagRule("flap"));
        }

        public override void Enter(BasicUsageExampleBase example)
        {
            scrambleTime = 0f;
            rollTime = 0f;
            if (scrambleModifier != null) scrambleModifier.Progress = 0f;
            if (rollingModifier != null) rollingModifier.Roll = 0f;
            if (flapModifier != null) flapModifier.Roll = 0f;
        }

        public override void Exit(BasicUsageExampleBase example)
        {
            if (scrambleModifier != null) scrambleModifier.Progress = 1f;
            if (rollingModifier != null) rollingModifier.Roll = 0f;
            if (flapModifier != null) flapModifier.Roll = 0f;
        }

        public override void Update(BasicUsageExampleBase example)
        {
            if (scrambleModifier != null)
            {
                scrambleTime += Time.deltaTime * 0.3f;
                scrambleModifier.Progress = Mathf.Clamp01(Mathf.Repeat(scrambleTime, 1.5f));
            }

            rollTime += Time.deltaTime;
            var settle = 1f - Mathf.Clamp01(Mathf.Repeat(rollTime, 4.5f) / 3f);
            var value = 12f * settle * settle;
            if (rollingModifier != null) rollingModifier.Roll = value;
            if (flapModifier != null) flapModifier.Roll = value;
        }
    }
}
