namespace LightSide
{
    internal sealed class WholeTextRangeSource : RangeSource
    {
        private readonly string identity;

        public RangeId RangeId { get; }
        public RangeSegmentId SegmentId { get; }

        public override string Identity => identity;

        public WholeTextRangeSource(string identity)
        {
            this.identity = identity;
            RangeId = AllocateRangeId();
            SegmentId = AllocateSegmentId();
        }
    }
}
