using System;
using System.Linq;
using System.Reflection;

namespace JeffGrawAssets.FlexibleUI
{
    public readonly struct BlurAlgorithm : IEquatable<BlurAlgorithm>
    {
        public readonly string Name;
        public readonly int FirstKernelIdx, SecondKernelIdx;

        public static BlurAlgorithm Tap3Checkerboard    { get; } = new("3-Tap Checkerboard", 0);
        public static BlurAlgorithm Tap4Corners         { get; } = new("4-Tap Corners", 1);
        public static BlurAlgorithm Tap4Cross           { get; } = new("4-Tap Cross", 2);
        public static BlurAlgorithm Tap5Star            { get; } = new("5-Tap Star", 3);
        public static BlurAlgorithm Tap7Hexagonal       { get; } = new("7-Tap Hexagonal", 4);
        public static BlurAlgorithm Tap8CornersAndCross { get; } = new("8-Tap Corners+Cross", 5);
        public static BlurAlgorithm Tap9Octagonal       { get; } = new("9-Tap Octagonal", 6);
        public static BlurAlgorithm Polynomial          { get; } = new("Quadratic", 7, 8);
        public static BlurAlgorithm Gaussian            { get; } = new("Gaussian", 9, 10);

        public static BlurAlgorithm[] All { get; }
        public static string[] Names { get; }

        private BlurAlgorithm(string name, int firstKernelIdx, int secondKernelIdx = -1)
            => (Name, FirstKernelIdx, SecondKernelIdx) = (name, firstKernelIdx, secondKernelIdx);

        static BlurAlgorithm()
        {
            var properties = typeof(BlurAlgorithm)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(BlurAlgorithm))
                .Select(p => (BlurAlgorithm)p.GetValue(null))
                .ToArray();

            All = properties;
            Names = properties.Select(a => a.Name).ToArray();
        }

        public bool Equals(BlurAlgorithm other) => FirstKernelIdx == other.FirstKernelIdx && SecondKernelIdx == other.SecondKernelIdx;
        public override bool Equals(object obj) => obj is BlurAlgorithm other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(FirstKernelIdx, SecondKernelIdx);
        public static bool operator ==(BlurAlgorithm l, BlurAlgorithm r) => l.FirstKernelIdx == r.FirstKernelIdx && l.SecondKernelIdx == r.SecondKernelIdx;
        public static bool operator !=(BlurAlgorithm l, BlurAlgorithm r) => l.FirstKernelIdx != r.FirstKernelIdx || l.SecondKernelIdx != r.SecondKernelIdx;
    }
}