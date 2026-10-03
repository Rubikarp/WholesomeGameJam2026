using System;

namespace LightSide
{
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class BoldParseRule : TagParseRule { public override string TagName => "b"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ItalicParseRule : TagParseRule { public override string TagName => "i"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ColorParseRule : TagParseRule { public override string TagName => "color"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class SizeParseRule : TagParseRule { public override string TagName => "size"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class UnderlineParseRule : TagParseRule { public override string TagName => "u"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class StrikethroughParseRule : TagParseRule { public override string TagName => "s"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class CSpaceParseRule : TagParseRule { public override string TagName => "cspace"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class LineSpacingParseRule : TagParseRule { public override string TagName => "line-spacing"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class LineHeightParseRule : TagParseRule { public override string TagName => "line-height"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class OutlineParseRule : TagParseRule { public override string TagName => "outline"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ShadowParseRule : TagParseRule { public override string TagName => "shadow"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class ObjParseRule : TagParseRule { public override string TagName => "obj"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class EllipsisTagRule : TagParseRule { public override string TagName => "ellipsis"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class UppercaseParseRule : TagParseRule { public override string TagName => "upper"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class GradientParseRule : TagParseRule { public override string TagName => "gradient"; }
    [Serializable, HideFromTypeSelector, Obsolete] internal sealed class LinkTagParseRule : TagParseRule { public override string TagName => "link"; }
}
