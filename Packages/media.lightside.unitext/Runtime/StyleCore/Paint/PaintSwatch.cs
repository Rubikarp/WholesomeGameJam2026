using System;

namespace LightSide
{
    /// <summary>
    /// A named paint in a <see cref="UniTextPaints"/> — a solid colour, gradient, or texture,
    /// plus how it projects and composites. <see cref="paint"/> is the reusable authored appearance,
    /// while <see cref="mapping"/> selects its text bounds.
    /// The swatch is the weakest layer of the projection override hierarchy (swatch → modifier
    /// fields → default parameters → tag attributes); an inherited mapping resolves to Block.
    /// </summary>
    [Serializable]
    public struct PaintSwatch : IStateSnapshot<PaintSwatch>
    {
        /// <summary>Name referenced by paint parameters and markup.</summary>
        public string name;

        /// <summary>Authored source, projection, and compositing mode.</summary>
        public Paint paint;

        /// <summary>Text-specific bounds owner used to build the projection frame.</summary>
        public PaintMapping mapping;

        /// <summary>Captures the swatch with detached gradient-stop storage.</summary>
        public PaintSwatch CaptureStateSnapshot()
        {
            var snapshot = this;
            snapshot.paint = paint.CaptureStateSnapshot();
            return snapshot;
        }

        /// <summary>Compares all authored swatch state, including gradient contents.</summary>
        public bool StateEquals(in PaintSwatch snapshot)
        {
            var snapshotPaint = snapshot.paint;
            return string.Equals(name, snapshot.name, StringComparison.Ordinal) &&
                   paint.StateEquals(in snapshotPaint) &&
                   mapping == snapshot.mapping;
        }
    }
}
