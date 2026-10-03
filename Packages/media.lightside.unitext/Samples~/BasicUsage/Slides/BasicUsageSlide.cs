using UnityEngine;

namespace LightSide.Samples
{
    internal abstract class BasicUsageSlide
    {
        public abstract string Text { get; }

        /// <summary>Runs once, right before the slide is first shown; creates the styles and state the slide's markup needs.</summary>
        public virtual void Register(BasicUsageExampleBase example) { }
        public virtual void Enter(BasicUsageExampleBase example) { }
        public virtual void Exit(BasicUsageExampleBase example) { }
        public virtual void Update(BasicUsageExampleBase example) { }
        public virtual bool HandleLink(BasicUsageExampleBase example, string url) => false;
        public virtual bool HasRangeAt(int cluster) => false;
        public virtual void Dispose(BasicUsageExampleBase example) { }
    }

    internal abstract class WidthAnimatedSlide : BasicUsageSlide
    {
        private float time;

        public override void Enter(BasicUsageExampleBase example)
        {
            time = 0f;
        }

        public override void Exit(BasicUsageExampleBase example)
        {
            example.SetContainerWidth(example.DefaultContainerWidth);
        }

        public override void Update(BasicUsageExampleBase example)
        {
            if (example.DemoContainer == null) return;
            time += Time.deltaTime;
            example.SetContainerWidth(Mathf.Lerp(260f, example.DefaultContainerWidth, Mathf.PingPong(time * 0.22f, 1f)));
        }
    }
}
