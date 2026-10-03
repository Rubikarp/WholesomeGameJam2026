using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Platform-neutral meaning exposed by one text range entity.</summary>
    public enum TextSemanticRole : byte
    {
        /// <summary>Generic annotated text without an interaction-specific role.</summary>
        Text,
        /// <summary>Navigable hyperlink.</summary>
        Link,
        /// <summary>Action resembling a button.</summary>
        Button,
        /// <summary>Person, role or named mention.</summary>
        Mention,
        /// <summary>Topic or hashtag reference.</summary>
        Tag,
        /// <summary>Source code or another literal computer-language fragment.</summary>
        Code,
        /// <summary>Mathematical expression.</summary>
        Math,
        /// <summary>Heading in a document hierarchy.</summary>
        Heading,
        /// <summary>Inline image or non-text object.</summary>
        Image,
        /// <summary>Comment or annotation attached to document content.</summary>
        Comment,
        /// <summary>Spelling, grammar or validation issue.</summary>
        Error,
        /// <summary>Status or live-region style information.</summary>
        Status,
    }

    /// <summary>Independent semantic states announced by accessibility adapters.</summary>
    [Flags]
    public enum TextSemanticStates
    {
        /// <summary>No additional state.</summary>
        None = 0,
        /// <summary>The entity rejects interaction.</summary>
        Disabled = 1 << 0,
        /// <summary>The entity is selected by document or application state.</summary>
        Selected = 1 << 1,
        /// <summary>The entity currently owns keyboard/gamepad focus.</summary>
        Focused = 1 << 2,
        /// <summary>The entity is expanded.</summary>
        Expanded = 1 << 3,
        /// <summary>The entity is checked or toggled on.</summary>
        Checked = 1 << 4,
        /// <summary>An asynchronous operation is in progress.</summary>
        Busy = 1 << 5,
        /// <summary>The represented value is invalid.</summary>
        Invalid = 1 << 6,
        /// <summary>Sensitive or spoiler content is concealed.</summary>
        Concealed = 1 << 7,
        /// <summary>A navigable target has previously been visited.</summary>
        Visited = 1 << 8,
        /// <summary>The content can be inspected but not edited.</summary>
        ReadOnly = 1 << 9,
    }

    /// <summary>Actions a platform bridge or project accessibility layer may request.</summary>
    [Flags]
    public enum TextSemanticActions
    {
        /// <summary>No invokable action.</summary>
        None = 0,
        /// <summary>Invoke the entity's normal activation.</summary>
        Activate = 1 << 0,
        /// <summary>Request contextual actions.</summary>
        Context = 1 << 1,
        /// <summary>Expand concealed or collapsed content.</summary>
        Expand = 1 << 2,
        /// <summary>Collapse expanded content.</summary>
        Collapse = 1 << 3,
        /// <summary>Copy the entity's represented value.</summary>
        Copy = 1 << 4,
        /// <summary>Increment a range-like value.</summary>
        Increment = 1 << 5,
        /// <summary>Decrement a range-like value.</summary>
        Decrement = 1 << 6,
    }

    /// <summary>Stable platform-neutral accessibility node for one logical range entity.</summary>
    public sealed class TextSemanticNode
    {
        private RangeSegment[] segments;
        private object payload;

        /// <summary>Stable source-scoped entity identity.</summary>
        public RangeIdentity Identity { get; }
        /// <summary>Optional stable channel shared with interaction routing.</summary>
        public RangeChannel Channel { get; private set; }
        /// <summary>Current typed semantic payload.</summary>
        public RangePayloadView Payload => new(payload);
        /// <summary>Every contiguous or point-like segment represented by this single node.</summary>
        public ReadOnlySpan<RangeSegment> Segments => segments;
        /// <summary>Meaning announced for the entity.</summary>
        public TextSemanticRole Role { get; private set; }
        /// <summary>Accessible name.</summary>
        public string Label { get; private set; }
        /// <summary>Current represented value.</summary>
        public string Value { get; private set; }
        /// <summary>Additional usage guidance.</summary>
        public string Hint { get; private set; }
        /// <summary>BCP 47 language tag used for pronunciation, or null to inherit.</summary>
        public string Language { get; private set; }
        /// <summary>Independent semantic states.</summary>
        public TextSemanticStates States { get; private set; }
        /// <summary>Actions accepted by <see cref="UniTextSemantics.PerformAction"/>.</summary>
        public TextSemanticActions Actions { get; private set; }
        /// <summary>Union of final component-local segment bounds.</summary>
        public Rect Bounds { get; private set; }
        /// <summary>Rendered-text revision used to build segments and bounds.</summary>
        public TextRevision Revision { get; private set; }

        internal TextSemanticNode(RangeIdentity identity, RangeChannel channel, object payload,
            RangeSegment[] segments, TextSemanticRole role, string label, string value,
            string hint, string language, TextSemanticStates states, TextSemanticActions actions,
            Rect bounds, TextRevision revision)
        {
            Identity = identity;
            Channel = channel;
            this.payload = payload;
            this.segments = segments;
            Role = role;
            Label = label;
            Value = value;
            Hint = hint;
            Language = language;
            States = states;
            Actions = actions;
            Bounds = bounds;
            Revision = revision;
        }

        internal bool Update(RangeChannel candidateChannel, object candidatePayload,
            IReadOnlyList<RangeSegment> candidateSegments, TextSemanticRole candidateRole,
            string candidateLabel, string candidateValue, string candidateHint,
            string candidateLanguage, TextSemanticStates candidateStates,
            TextSemanticActions candidateActions, Rect candidateBounds,
            TextRevision candidateRevision, out bool topologyChanged)
        {
            var segmentsChanged = segments.Length != candidateSegments.Count;
            if (!segmentsChanged)
            {
                for (var i = 0; i < segments.Length; i++)
                {
                    var candidate = candidateSegments[i];
                    if (segments[i].Id == candidate.Id &&
                        segments[i].Range.start == candidate.Range.start &&
                        segments[i].Range.length == candidate.Range.length) continue;
                    segmentsChanged = true;
                    break;
                }
            }
            topologyChanged = segmentsChanged;

            var changed = segmentsChanged || Channel != candidateChannel ||
                          !ReferenceEquals(payload, candidatePayload) || Role != candidateRole ||
                          Label != candidateLabel || Value != candidateValue ||
                          Hint != candidateHint || Language != candidateLanguage ||
                          States != candidateStates || Actions != candidateActions ||
                          Bounds != candidateBounds || Revision != candidateRevision;
            if (!changed) return false;

            if (segmentsChanged)
            {
                segments = new RangeSegment[candidateSegments.Count];
                for (var i = 0; i < segments.Length; i++) segments[i] = candidateSegments[i];
            }
            Channel = candidateChannel;
            payload = candidatePayload;
            Role = candidateRole;
            Label = candidateLabel;
            Value = candidateValue;
            Hint = candidateHint;
            Language = candidateLanguage;
            States = candidateStates;
            Actions = candidateActions;
            Bounds = candidateBounds;
            Revision = candidateRevision;
            return true;
        }

        internal bool SetStates(TextSemanticStates states)
        {
            if (States == states) return false;
            States = states;
            return true;
        }
    }

    /// <summary>Kind of incremental semantic-tree change.</summary>
    public enum TextSemanticChangeKind : byte
    {
        /// <summary>A node appeared.</summary>
        Added,
        /// <summary>An existing node changed content, geometry or state.</summary>
        Updated,
        /// <summary>A node disappeared.</summary>
        Removed,
    }

    /// <summary>One incremental change emitted after a semantic tree commit.</summary>
    public readonly struct TextSemanticChange
    {
        /// <summary>Change kind.</summary>
        public TextSemanticChangeKind Kind { get; }
        /// <summary>Current node for Added/Updated, or the removed previous node.</summary>
        public TextSemanticNode Node { get; }

        internal TextSemanticChange(TextSemanticChangeKind kind, TextSemanticNode node)
        {
            Kind = kind;
            Node = node;
        }
    }

    /// <summary>Mutable request routed before a semantic action's built-in interaction behavior.</summary>
    public sealed class TextSemanticActionRequest
    {
        /// <summary>Node receiving the action.</summary>
        public TextSemanticNode Node { get; }
        /// <summary>Single requested action.</summary>
        public TextSemanticActions Action { get; }
        /// <summary>Whether a project or bridge handler performed the action.</summary>
        public bool Handled { get; set; }
        /// <summary>Whether built-in activation/context routing must be skipped.</summary>
        public bool DefaultPrevented { get; private set; }

        internal TextSemanticActionRequest(TextSemanticNode node, TextSemanticActions action)
        {
            Node = node;
            Action = action;
        }

        /// <summary>Marks the request handled without canceling its built-in behavior.</summary>
        public void Handle() => Handled = true;
        /// <summary>Cancels built-in behavior without implicitly marking the request handled.</summary>
        public void PreventDefault() => DefaultPrevented = true;
    }

    /// <summary>Signature for project or native adapters handling a declared semantic action.</summary>
    public delegate void TextSemanticActionHandler(TextSemanticActionRequest request);
}
