using System;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Play/pause and stop controls over a playable subject, tracking its state on its own.
    /// Additional controls added to the bar sit after them.
    /// </summary>
    /// <remarks>
    /// Playback state is polled rather than observed, because it advances without an editor event.
    /// </remarks>
    public sealed class InspectorTransportBar : InspectorRow
    {
        private const long PollInterval = 200;

        private readonly Func<bool> isPlaying;
        private readonly InspectorTransportGlyph glyph;
        private readonly Button playPause;

        /// <summary>
        /// Creates the bar over the callbacks that read and drive the subject. <paramref name="toggle"/>
        /// receives true to start playing and false to pause.
        /// </summary>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public InspectorTransportBar(Func<bool> isPlaying, Action<bool> toggle, Action stop)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            this.isPlaying = isPlaying ?? throw new ArgumentNullException(nameof(isPlaying));

            playPause = InspectorTransportGlyph.CreateButton(
                InspectorTransportGlyph.Shape.Play, "Play / Pause", () =>
                {
                    toggle(!isPlaying());
                    Refresh();
                }, out glyph);
            var stopButton = InspectorTransportGlyph.CreateButton(
                InspectorTransportGlyph.Shape.Stop, "Stop", () =>
                {
                    stop();
                    Refresh();
                }, out _);
            stopButton.AddToClassList("lightside-button--danger");
            Add(playPause);
            Add(stopButton);

            Refresh();
            playPause.schedule.Execute(Refresh).Every(PollInterval);
        }

        /// <summary>Creates the bar over a timeline's own transport.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
        public InspectorTransportBar(ITimelineTransport transport)
            : this(TransportState(transport),
                playing =>
                {
                    if (playing) transport.Play();
                    else transport.Pause();
                },
                () => transport.Stop())
        {
        }

        /// <summary>Matches the play glyph and its warning accent to the subject's current state.</summary>
        public void Refresh()
        {
            var playing = isPlaying();
            glyph.Set(playing
                ? InspectorTransportGlyph.Shape.Pause
                : InspectorTransportGlyph.Shape.Play);
            playPause.EnableInClassList("lightside-button--warning", playing);
        }

        private static Func<bool> TransportState(ITimelineTransport transport)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            return () => transport.IsPlaying;
        }
    }
}
