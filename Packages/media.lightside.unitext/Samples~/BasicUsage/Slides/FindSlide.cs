using System;
using UnityEngine;

namespace LightSide.Samples
{
    internal sealed class FindSlide : BasicUsageSlide
    {
        public override string Text =>
            "🔍 <b>Find-in-Page (RangeSource + HighlightModifier)</b>\n\n" +
            "The quick brown fox jumps over the lazy dog. The dog naps while the fox runs; " +
            "a fox, a dog, and another fox share the yard.\n\n" +
            "Find: <link=find:fox><color=#4ECDC4><u>fox</u></color></link>  " +
            "<link=find:dog><color=#4ECDC4><u>dog</u></color></link>  " +
            "<link=find:the><color=#4ECDC4><u>the</u></color></link>   " +
            "Navigate: <link=findprev><color=#4ECDC4><u>◀ prev</u></color></link>  " +
            "<link=findnext><color=#4ECDC4><u>next ▶</u></color></link>  " +
            "<link=findclear><color=#4ECDC4><u>clear</u></color></link>\n\n" +
            "<size=72%><color=#888>FindAll writes entities into two MutableRangeSources. " +
            "Both are ordinary Styles with HighlightModifier, so Paint, gradients, textures, " +
            "GeometryMapping and priority work exactly like every other modifier.</color></size>";

        private readonly TextRange[] matches = new TextRange[64];
        private readonly MutableRangeSource matchesSource = new();
        private readonly MutableRangeSource currentSource = new();
        private int matchCount;
        private int currentIndex;
        private string query;

        public override void Register(BasicUsageExampleBase example)
        {
            matchesSource.SetIdentity("basic-usage.find.matches");
            currentSource.SetIdentity("basic-usage.find.current");
            example.AddStyle(new HighlightModifier
            {
                Paint = PaintRef.Solid(new Color32(255, 210, 60, 90)),
                GeometryMapping = GeometryMapping.Range,
                CornerRadius = UnitValue.Em(0.25f),
            }, matchesSource);
            example.AddStyle(new HighlightModifier
            {
                Paint = PaintRef.Solid(new Color32(255, 140, 0, 160)),
                GeometryMapping = GeometryMapping.Range,
                CornerRadius = UnitValue.Em(0.25f),
                Priority = RangeDecorationPriorities.FindCurrent,
            }, currentSource);
        }

        public override void Exit(BasicUsageExampleBase example) => Clear();

        public override bool HandleLink(BasicUsageExampleBase example, string url)
        {
            if (url.StartsWith("find:"))
            {
                Find(example, url.Substring(5));
                return true;
            }
            if (url == "findnext")
            {
                Cycle(example, 1);
                return true;
            }
            if (url == "findprev")
            {
                Cycle(example, -1);
                return true;
            }
            if (url != "findclear") return false;
            Clear();
            example.UpdateStatus("<color=#2ECC71>Find:</color> cleared");
            return true;
        }

        private void Find(BasicUsageExampleBase example, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            query = value;
            matchCount = example.DemoText.FindAll(value, StringComparison.OrdinalIgnoreCase, matches);
            using (var update = matchesSource.BeginUpdate(matchesSource.Snapshot.Revision))
            {
                update.Clear();
                for (var i = 0; i < matchCount; i++) update.Add(matches[i]);
                update.Commit();
            }
            currentIndex = 0;
            UpdateCurrent(example);
        }

        private void Cycle(BasicUsageExampleBase example, int direction)
        {
            if (matchCount == 0) return;
            currentIndex = (currentIndex + direction + matchCount) % matchCount;
            UpdateCurrent(example);
        }

        private void UpdateCurrent(BasicUsageExampleBase example)
        {
            using (var update = currentSource.BeginUpdate(currentSource.Snapshot.Revision))
            {
                update.Clear();
                if (matchCount > 0) update.Add(matches[currentIndex]);
                update.Commit();
            }
            if (matchCount == 0)
            {
                example.UpdateStatus($"<color=#E74C3C>Find:</color> \"{query}\" — no matches");
                return;
            }
            example.UpdateStatus(
                $"<color=#2ECC71>Find:</color> \"{query}\" — match {currentIndex + 1}/{matchCount}");
        }

        private void Clear()
        {
            query = null;
            matchCount = 0;
            currentIndex = 0;
            Clear(matchesSource);
            Clear(currentSource);
        }

        private static void Clear(MutableRangeSource source)
        {
            var snapshot = source.Snapshot;
            if (!snapshot.IsValid || source.Count == 0) return;
            using var update = source.BeginUpdate(snapshot.Revision);
            update.Clear();
            update.Commit();
        }
    }
}
