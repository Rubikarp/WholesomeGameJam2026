using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Moves each font asset's embedded <c>fontData</c> blob out of the font object into a hidden
    /// <see cref="UniTextFontPayload"/> sub-asset document, points the font's <c>payload</c>
    /// reference at it and stamps the font with the payload's source identity. Documents whose
    /// <c>fontData</c> is empty only lose the retired field; documents already holding a payload
    /// reference without the identity stamp receive the stamp from their payload document.
    /// </summary>
    internal sealed class FontDataToPayloadMigration : IMigration
    {
        private const string PayloadScriptGuid = "3ada6a5f24f744908bf15148b57c5fb7";
        private const string FontScriptGuid = "f5c059c895b0b3446b609a9e8122a187";
        private const string ColorFontScriptGuid = "48bae79c086d01843a03eb11551b8052";
        private const string VariantScriptGuid = "f98884f63101cdb43a545305c46a5ba2";
        private const string SystemFontScriptGuid = "29d993da439e04a4b9da5e0cfc34be86";

        private readonly string[] tokens =
        {
            MigrationTokens.Script(FontScriptGuid),
            MigrationTokens.Script(ColorFontScriptGuid),
            MigrationTokens.Script(VariantScriptGuid),
            MigrationTokens.Script(SystemFontScriptGuid),
        };

        public string Id => "move/UniTextFont.fontData->UniTextFontPayload";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            HashSet<long> usedFileIds = null;

            foreach (var document in ctx.Documents)
            {
                var scriptGuid = document.ScriptGuid;
                if (scriptGuid is not (FontScriptGuid or ColorFontScriptGuid
                    or VariantScriptGuid or SystemFontScriptGuid)) continue;

                var body = document.Body;
                var entry = body?.Entry("fontData");
                if (entry == null)
                {
                    StampMissingIdentity(ctx, body);
                    continue;
                }

                var hex = entry.Value?.Scalar;
                if (string.IsNullOrEmpty(hex))
                {
                    ctx.Edit.Delete(entry.Start, entry.End);
                    continue;
                }

                var stored = FromHex(ctx, hex);
                var storedHex = hex;
                if (!Zstd.IsCompressed(stored))
                {
                    stored = Zstd.Compress(stored);
                    storedHex = FontSourceId.ToHex(stored);
                }

                var token = ReadToken(body, stored);
                var rawLength = checked((int)Zstd.GetFrameContentSize(stored));
                byte[] sourceHash;
                using (var sha = SHA256.Create()) sourceHash = sha.ComputeHash(stored);

                usedFileIds ??= CollectFileIds(ctx.Documents);
                var fileId = 8_600_000_000L + unchecked((uint)token);
                while (!usedFileIds.Add(fileId)) fileId++;

                var sourceHashHex = FontSourceId.ToHex(sourceHash);
                ctx.Edit.Replace(entry.Start, entry.End,
                    $"  payload: {{fileID: {fileId}}}\n"
                    + $"  payloadSourceHash: {sourceHashHex}\n"
                    + $"  payloadRawLength: {rawLength.ToString(CultureInfo.InvariantCulture)}\n");
                AppendPayloadDocument(ctx, body, fileId, token, rawLength, sourceHashHex, storedHex);
            }
        }

        private static void StampMissingIdentity(MigrationContext ctx, YamlNode body)
        {
            var reference = body?.Entry("payload");
            if (reference == null || body.Entry("payloadSourceHash") != null) return;

            var fileIdScalar = reference.Value?["fileID"]?.Scalar;
            if (!long.TryParse(fileIdScalar, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var fileId) || fileId == 0) return;

            YamlNode payloadBody = null;
            foreach (var document in ctx.Documents)
                if (document.FileId == fileId)
                {
                    payloadBody = document.Body;
                    break;
                }

            var sourceHash = payloadBody?["sourceHash"]?.Scalar;
            var rawLength = payloadBody?["rawLength"]?.Scalar;
            if (sourceHash is not { Length: 64 } || string.IsNullOrEmpty(rawLength))
                throw new InvalidOperationException(
                    $"[UniText] Cannot migrate '{ctx.AssetPath}': the payload reference points at"
                    + $" document {fileId}, which carries no source identity.");

            ctx.Edit.Insert(reference.End,
                $"  payloadSourceHash: {sourceHash}\n  payloadRawLength: {rawLength}\n");
        }

        private static void AppendPayloadDocument(MigrationContext ctx, YamlNode fontBody,
            long fileId, int token, int rawLength, string sourceHashHex, string dataHex)
        {
            var nameValue = fontBody.Entry("m_Name")?.Value;
            var name = nameValue == null
                ? ""
                : ctx.Edit.Slice(nameValue.Start, nameValue.End).TrimEnd('\r', '\n');

            var text = new StringBuilder(dataHex.Length + 512);
            if (ctx.Edit.Source.Length > 0 && ctx.Edit.Source[^1] != '\n') text.Append('\n');
            text.Append("--- !u!114 &").Append(fileId).Append('\n')
                .Append("MonoBehaviour:\n")
                .Append("  m_ObjectHideFlags: 1\n")
                .Append("  m_CorrespondingSourceObject: {fileID: 0}\n")
                .Append("  m_PrefabInstance: {fileID: 0}\n")
                .Append("  m_PrefabAsset: {fileID: 0}\n")
                .Append("  m_GameObject: {fileID: 0}\n")
                .Append("  m_Enabled: 1\n")
                .Append("  m_EditorHideFlags: 0\n")
                .Append("  m_Script: {fileID: 11500000, guid: ").Append(PayloadScriptGuid)
                .Append(", type: 3}\n")
                .Append("  m_Name: ").Append(name).Append('\n')
                .Append("  m_EditorClassIdentifier: LightSide.UniText::LightSide.UniTextFontPayload\n")
                .Append("  token: ").Append(token.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("  rawLength: ").Append(rawLength.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("  sourceHash: ").Append(sourceHashHex).Append('\n')
                .Append("  data: ").Append(dataHex).Append('\n');
            ctx.Edit.Insert(ctx.Edit.Source.Length, text.ToString());
        }

        private static int ReadToken(YamlNode body, byte[] stored)
        {
            var scalar = body["fontDataHash"]?.Scalar;
            if (scalar != null
                && int.TryParse(scalar, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var token)
                && token != 0)
                return token;
            return UniTextFont.ComputeFontDataHash(Zstd.Decompress(stored));
        }

        private static HashSet<long> CollectFileIds(IReadOnlyList<YamlDocument> documents)
        {
            var result = new HashSet<long>();
            for (var i = 0; i < documents.Count; i++) result.Add(documents[i].FileId);
            return result;
        }

        private static byte[] FromHex(MigrationContext ctx, string hex)
        {
            if ((hex.Length & 1) != 0)
                throw new InvalidOperationException(
                    $"[UniText] Cannot migrate '{ctx.AssetPath}': fontData holds an odd-length hex string.");
            var result = new byte[hex.Length / 2];
            for (var i = 0; i < result.Length; i++)
            {
                int high = HexValue(hex[i * 2]);
                int low = HexValue(hex[i * 2 + 1]);
                if (high < 0 || low < 0)
                    throw new InvalidOperationException(
                        $"[UniText] Cannot migrate '{ctx.AssetPath}': fontData holds a non-hex character.");
                result[i] = (byte)((high << 4) | low);
            }
            return result;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
    }
}
