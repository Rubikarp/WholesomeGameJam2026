using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Plans the TMP rewrite of one C# file. The file is read as a lexical model first, so string
    /// literals, comments and TMP-conditional preprocessor regions are never edited, and a member
    /// is renamed only where the receiver is known to hold a TMP type declared in the same file.
    /// Everything it cannot resolve is reported instead of guessed.
    /// </summary>
    internal static class ScriptRewriter
    {
        private static readonly HashSet<string> componentGetters = new(StringComparer.Ordinal)
        {
            "GetComponent",
            "GetComponentInChildren",
            "GetComponentInParent",
            "AddComponent",
            "GetComponents",
            "GetComponentsInChildren",
            "GetComponentsInParent",
        };

        private static readonly HashSet<string> declarationStoppers = new(StringComparer.Ordinal)
        {
            "return", "new", "is", "as", "case", "in", "out", "ref", "typeof", "default",
            "this", "base", "null", "true", "false",
        };

        private static readonly Dictionary<(Type type, string member), bool> renameSurvivors = new();

        private const string LightSideNamespace = "LightSide";

        private static HashSet<string> lightSideTypeNames;

        /// <summary>
        /// Every type name <c>using LightSide;</c> brings into scope, across the assemblies loaded
        /// in the editor. Generic types appear under their source name, without the arity tick.
        /// </summary>
        private static HashSet<string> LightSideTypeNames()
        {
            if (lightSideTypeNames != null) return lightSideTypeNames;

            var names = new HashSet<string>(StringComparer.Ordinal);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].IsDynamic) continue;
                Type[] types;
                try { types = assemblies[i].GetExportedTypes(); }
                catch (ReflectionTypeLoadException partial) { types = partial.Types; }
                for (var t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type == null || type.Namespace != LightSideNamespace) continue;
                    var name = type.Name;
                    var tick = name.IndexOf('`');
                    names.Add(tick < 0 ? name : name.Substring(0, tick));
                }
            }

            lightSideTypeNames = names;
            return names;
        }

        /// <summary>
        /// Whether a member of that name still resolves on the UniText type after the rename because
        /// Unity declares it on a shared base — <c>name</c>, <c>hideFlags</c>, <c>GetInstanceID</c>.
        /// A member the tables do not name and that does not survive would leave the call unresolved.
        /// </summary>
        private static bool SurvivesRename(Type target, string member)
        {
            var key = (target, member);
            if (renameSurvivors.TryGetValue(key, out var cached)) return cached;

            var survives = false;
            var found = target.GetMember(member,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.FlattenHierarchy);
            for (var i = 0; i < found.Length && !survives; i++)
            {
                var declaring = found[i].DeclaringType;
                survives = declaring == typeof(UnityEngine.Object) ||
                           declaring == typeof(ScriptableObject) ||
                           declaring == typeof(Component) ||
                           declaring == typeof(Behaviour) ||
                           declaring == typeof(MonoBehaviour);
            }
            renameSurvivors[key] = survives;
            return survives;
        }

        /// <summary>Every edit and report the rewrite has for <paramref name="content"/>.</summary>
        public static List<ScriptReplacement> Analyze(string content)
        {
            var pass = new Pass(content);
            pass.Run();
            return pass.Results;
        }

        private struct Interpolation
        {
            public bool Verbatim;
            public int BraceDepth;
        }

        private sealed class Pass
        {
            private readonly string source;
            private readonly bool[] isCode;
            private readonly int[] lineStarts;
            private readonly List<(int start, int end)> identifiers = new();
            private readonly Dictionary<string, MigrationMapping.TmpTypeKind> declared = new(StringComparer.Ordinal);
            private readonly Dictionary<string, MigrationMapping.TmpTypeKind> collections = new(StringComparer.Ordinal);
            private readonly HashSet<int> inputFieldDeclarations = new();
            private readonly List<(int start, int end)> conditionalRegions = new();
            private readonly HashSet<string> unmappedSeen = new(StringComparer.Ordinal);
            private readonly HashSet<string> unresolvedSeen = new(StringComparer.Ordinal);
            private int conditional;
            private int conditionalStart = -1;
            private bool renamedAnything;
            private bool needsTmProUsing;
            private int usingTmProStart = -1;
            private int usingTmProEnd = -1;
            private int lastUsingLineEnd = -1;
            private bool hasLightSideUsing;
            private readonly bool qualifyLightSide;
            private readonly string collidingName;
            private readonly int collidingStart;
            private readonly int collidingEnd;

            public readonly List<ScriptReplacement> Results = new();

            public Pass(string source)
            {
                this.source = source;
                isCode = new bool[source.Length];
                lineStarts = BuildLineStarts(source);
                ScanLexically();
                CollectIdentifiers();
                qualifyLightSide = FindLightSideCollision(
                    out collidingName, out collidingStart, out collidingEnd);
            }

            /// <summary>
            /// The first name in this file that <c>using LightSide;</c> would capture from somewhere
            /// else. The file compiles today, so a name that also belongs to the LightSide namespace
            /// and that the rewrite does not introduce itself already resolves elsewhere; adding the
            /// directive would make it ambiguous. Such a file takes qualified type names instead.
            /// </summary>
            private bool FindLightSideCollision(out string name, out int start, out int end)
            {
                name = null;
                start = 0;
                end = 0;

                var lightSideTypes = LightSideTypeNames();
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (wordStart, wordEnd) = identifiers[i];
                    var word = source.Substring(wordStart, wordEnd - wordStart);
                    if (!lightSideTypes.Contains(word)) continue;
                    if (MigrationMapping.IntroducedScriptTypes.Contains(word)) continue;
                    if (IsMemberAccess(wordStart)) continue;

                    name = word;
                    start = wordStart;
                    end = wordEnd;
                    return true;
                }

                return false;
            }

            public void Run()
            {
                CollectDeclarations();
                RewriteTypes();
                RewriteMembers();
                ReportUnresolvedInputReferences();
                RewriteUsings();
                ReportConditionalRegions();
            }

            #region Lexical model

            private static int[] BuildLineStarts(string source)
            {
                var starts = new List<int> { 0 };
                for (var i = 0; i < source.Length; i++)
                    if (source[i] == '\n') starts.Add(i + 1);
                return starts.ToArray();
            }

            /// <summary>
            /// Marks every character that is real code. Comments, char and string literals stay
            /// unmarked; the holes of an interpolated string are code again, at any nesting depth.
            /// </summary>
            private void ScanLexically()
            {
                var interpolations = new List<Interpolation>();
                var index = 0;
                while (index < source.Length)
                {
                    if (interpolations.Count > 0 &&
                        interpolations[interpolations.Count - 1].BraceDepth == 0)
                    {
                        index = ScanInterpolatedText(interpolations, index);
                        continue;
                    }

                    var current = source[index];
                    if (current == '/' && Peek(index + 1) == '/')
                    {
                        while (index < source.Length && source[index] != '\n') index++;
                        continue;
                    }
                    if (current == '/' && Peek(index + 1) == '*')
                    {
                        index += 2;
                        while (index + 1 < source.Length &&
                               !(source[index] == '*' && source[index + 1] == '/')) index++;
                        index = Math.Min(source.Length, index + 2);
                        continue;
                    }
                    if (current == '#' && IsLineStart(index))
                    {
                        index = ScanDirective(index);
                        continue;
                    }
                    if (current == '\'')
                    {
                        index = SkipCharLiteral(index);
                        continue;
                    }
                    if (current == '"' || ((current == '@' || current == '$') && OpensString(index)))
                    {
                        index = ScanStringStart(interpolations, index);
                        continue;
                    }

                    if (conditional == 0) isCode[index] = true;
                    if (interpolations.Count > 0 && (current == '{' || current == '}'))
                    {
                        var top = interpolations[interpolations.Count - 1];
                        top.BraceDepth += current == '{' ? 1 : -1;
                        interpolations[interpolations.Count - 1] = top;
                    }
                    index++;
                }
                if (conditionalStart >= 0)
                    conditionalRegions.Add((conditionalStart, source.Length));
            }

            private char Peek(int index) => index < source.Length ? source[index] : '\0';

            private bool IsLineStart(int index)
            {
                for (var i = index - 1; i >= 0; i--)
                {
                    if (source[i] == '\n') return true;
                    if (!char.IsWhiteSpace(source[i])) return false;
                }
                return true;
            }

            /// <summary>
            /// Consumes one preprocessor line and tracks TMP-conditional regions, whose contents
            /// stay untouched: the branch a symbol selects is the author's decision, not ours.
            /// </summary>
            private int ScanDirective(int index)
            {
                var end = index;
                while (end < source.Length && source[end] != '\n') end++;
                var line = source.Substring(index, end - index);

                if (StartsWithDirective(line, "if"))
                {
                    if (conditional > 0) conditional++;
                    else if (MentionsTmp(line))
                    {
                        conditional = 1;
                        conditionalStart = index;
                    }
                }
                else if (StartsWithDirective(line, "endif") && conditional > 0)
                {
                    conditional--;
                    if (conditional == 0)
                    {
                        conditionalRegions.Add((conditionalStart, end));
                        conditionalStart = -1;
                    }
                }
                return end;
            }

            private static bool StartsWithDirective(string line, string keyword)
            {
                var i = 1;
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                return string.CompareOrdinal(line, i, keyword, 0, keyword.Length) == 0;
            }

            private static bool MentionsTmp(string line)
                => line.IndexOf("TEXTMESHPRO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) >= 0;

            private int SkipCharLiteral(int index)
            {
                var i = index + 1;
                while (i < source.Length)
                {
                    if (source[i] == '\\') { i += 2; continue; }
                    if (source[i] == '\'' || source[i] == '\n') return i + 1;
                    i++;
                }
                return source.Length;
            }

            private bool OpensString(int index)
            {
                var i = index;
                while (i < source.Length && (source[i] == '@' || source[i] == '$')) i++;
                return i < source.Length && source[i] == '"';
            }

            private int ScanStringStart(List<Interpolation> interpolations, int index)
            {
                var interpolated = false;
                var verbatim = false;
                var i = index;
                while (i < source.Length && (source[i] == '@' || source[i] == '$'))
                {
                    if (source[i] == '$') interpolated = true;
                    else verbatim = true;
                    i++;
                }
                if (i >= source.Length || source[i] != '"') return index + 1;

                var run = 0;
                while (i + run < source.Length && source[i + run] == '"') run++;
                i += run;

                if (run >= 3) return SkipRawString(i, run);
                if (interpolated)
                {
                    interpolations.Add(new Interpolation { Verbatim = verbatim });
                    return i;
                }
                return SkipPlainString(i, verbatim);
            }

            private int SkipRawString(int index, int quoteRun)
            {
                var i = index;
                while (i < source.Length)
                {
                    if (source[i] != '"') { i++; continue; }
                    var closing = 0;
                    while (i + closing < source.Length && source[i + closing] == '"') closing++;
                    if (closing >= quoteRun) return i + closing;
                    i += closing;
                }
                return source.Length;
            }

            private int SkipPlainString(int index, bool verbatim)
            {
                var i = index;
                while (i < source.Length)
                {
                    var current = source[i];
                    if (verbatim)
                    {
                        if (current == '"')
                        {
                            if (Peek(i + 1) == '"') { i += 2; continue; }
                            return i + 1;
                        }
                        i++;
                        continue;
                    }
                    if (current == '\\') { i += 2; continue; }
                    if (current == '"' || current == '\n') return i + 1;
                    i++;
                }
                return source.Length;
            }

            /// <summary>Advances through the literal part of an interpolated string, opening holes.</summary>
            private int ScanInterpolatedText(List<Interpolation> interpolations, int index)
            {
                var last = interpolations.Count - 1;
                var top = interpolations[last];
                var current = source[index];

                if (top.Verbatim)
                {
                    if (current == '"')
                    {
                        if (Peek(index + 1) == '"') return index + 2;
                        interpolations.RemoveAt(last);
                        return index + 1;
                    }
                }
                else
                {
                    if (current == '\\') return index + 2;
                    if (current == '"' || current == '\n')
                    {
                        interpolations.RemoveAt(last);
                        return index + 1;
                    }
                }

                if (current == '{')
                {
                    if (Peek(index + 1) == '{') return index + 2;
                    // The hole's closing brace is marked by the main loop; leaving this one
                    // unmarked would unbalance every depth walk that starts inside the hole.
                    if (conditional == 0) isCode[index] = true;
                    top.BraceDepth = 1;
                    interpolations[last] = top;
                    return index + 1;
                }
                if (current == '}' && Peek(index + 1) == '}') return index + 2;
                return index + 1;
            }

            private void CollectIdentifiers()
            {
                var i = 0;
                while (i < source.Length)
                {
                    if (!isCode[i] || !IsIdentifierStart(source[i])) { i++; continue; }
                    var start = i;
                    while (i < source.Length && isCode[i] && IsIdentifierPart(source[i])) i++;
                    identifiers.Add((start, i));
                }
            }

            private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

            private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

            #endregion

            #region Navigation

            private int NextSignificant(int index)
            {
                var i = index;
                while (i < source.Length && (!isCode[i] || char.IsWhiteSpace(source[i]))) i++;
                return i;
            }

            private int PreviousSignificant(int index)
            {
                var i = index;
                while (i >= 0 && (!isCode[i] || char.IsWhiteSpace(source[i]))) i--;
                return i;
            }

            private string ReadIdentifierAt(int start, out int end)
            {
                end = start;
                if (start >= source.Length || !IsIdentifierStart(source[start])) return null;
                while (end < source.Length && isCode[end] && IsIdentifierPart(source[end])) end++;
                return source.Substring(start, end - start);
            }

            private string ReadIdentifierBefore(int index, out int start)
            {
                start = index;
                if (index < 0 || !isCode[index] || !IsIdentifierPart(source[index])) return null;
                var i = index;
                while (i >= 0 && isCode[i] && IsIdentifierPart(source[i])) i--;
                start = i + 1;
                if (!IsIdentifierStart(source[start])) return null;
                return source.Substring(start, index - start + 1);
            }

            /// <summary>Index of the bracket opening the group that closes at <paramref name="index"/>.</summary>
            private int MatchBackward(int index, char open, char close)
            {
                var depth = 0;
                for (var i = index; i >= 0; i--)
                {
                    if (!isCode[i]) continue;
                    if (source[i] == close) depth++;
                    else if (source[i] == open && --depth == 0) return i;
                }
                return -1;
            }

            /// <summary>Index of the bracket closing the group that opens at <paramref name="index"/>.</summary>
            private int MatchForward(int index, char open, char close)
            {
                var depth = 0;
                for (var i = index; i < source.Length; i++)
                {
                    if (!isCode[i]) continue;
                    if (source[i] == open) depth++;
                    else if (source[i] == close && --depth == 0) return i;
                }
                return -1;
            }

            #endregion

            #region Declarations

            private void CollectDeclarations()
            {
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (start, end) = identifiers[i];
                    var word = source.Substring(start, end - start);

                    if (MigrationMapping.ScriptTypes.TryGetValue(word, out var mapping))
                    {
                        if (!IsConstructedType(start)) DeclareFromType(end, mapping.Kind);
                        continue;
                    }
                    if (word == "var") DeclareFromVar(end);
                    else if (word == "foreach") DeclareFromForeach(end);
                }
            }

            /// <summary>
            /// Whether a type token names something being built rather than something being
            /// declared, so <c>new TextMeshProUGUI[count]</c> does not bind <c>count</c>.
            /// </summary>
            private bool IsConstructedType(int typeStart)
            {
                var before = PreviousSignificant(typeStart - 1);
                if (before < 0) return false;
                var keyword = ReadIdentifierBefore(before, out _);
                return keyword == "new" || keyword == "typeof" || keyword == "sizeof";
            }

            /// <summary>
            /// Binds the names a type token introduces, covering arrays, generic element types and
            /// several declarators on one line. Anything that is not a declaration — a cast, a
            /// generic argument, a static access — resolves to no name at all.
            /// </summary>
            private void DeclareFromType(int typeEnd, MigrationMapping.TmpTypeKind kind)
            {
                var isCollection = false;
                var i = typeEnd;
                while (i < source.Length)
                {
                    if (!isCode[i] || char.IsWhiteSpace(source[i])) { i++; continue; }
                    var current = source[i];
                    if (current == '[' || current == ']' || current == '>' || current == ',' || current == '?')
                    {
                        if (current == '[' || current == '>') isCollection = true;
                        i++;
                        continue;
                    }
                    break;
                }
                if (i >= source.Length || !IsIdentifierStart(source[i])) return;

                while (true)
                {
                    var name = ReadIdentifierAt(i, out var nameEnd);
                    if (name == null || declarationStoppers.Contains(name)) return;
                    if (isCollection) collections[name] = kind;
                    else declared[name] = kind;
                    if (kind == MigrationMapping.TmpTypeKind.InputField)
                        inputFieldDeclarations.Add(i);

                    var next = NextSignificant(nameEnd);
                    if (next >= source.Length) return;
                    if (source[next] == '=')
                    {
                        next = SkipInitializer(next + 1);
                        if (next >= source.Length) return;
                    }
                    if (source[next] != ',') return;
                    i = NextSignificant(next + 1);
                    if (i >= source.Length || !IsIdentifierStart(source[i])) return;
                }
            }

            /// <summary>Runs past one initializer to the comma or semicolon that ends its declarator.</summary>
            private int SkipInitializer(int index)
            {
                var depth = 0;
                for (var i = index; i < source.Length; i++)
                {
                    if (!isCode[i]) continue;
                    var current = source[i];
                    if (current == '(' || current == '[' || current == '{') depth++;
                    else if (current == ')' || current == ']' || current == '}') depth--;
                    else if (depth == 0 && (current == ',' || current == ';')) return i;
                }
                return source.Length;
            }

            private void DeclareFromVar(int varEnd)
            {
                var nameStart = NextSignificant(varEnd);
                var name = ReadIdentifierAt(nameStart, out var nameEnd);
                if (name == null || declarationStoppers.Contains(name)) return;

                var assign = NextSignificant(nameEnd);
                if (assign >= source.Length || source[assign] != '=') return;
                var end = SkipInitializer(assign + 1);
                var kind = KindFromExpression(assign + 1, end);
                if (kind.HasValue)
                {
                    declared[name] = kind.Value;
                    if (kind.Value == MigrationMapping.TmpTypeKind.InputField)
                        inputFieldDeclarations.Add(nameStart);
                }
            }

            private void DeclareFromForeach(int foreachEnd)
            {
                var open = NextSignificant(foreachEnd);
                if (open >= source.Length || source[open] != '(') return;
                var close = MatchForward(open, '(', ')');
                if (close < 0) return;

                var cursor = NextSignificant(open + 1);
                var declarator = ReadIdentifierAt(cursor, out var declaratorEnd);
                if (declarator != "var") return;

                cursor = NextSignificant(declaratorEnd);
                var nameStart = cursor;
                var name = ReadIdentifierAt(cursor, out var nameEnd);
                if (name == null) return;

                cursor = NextSignificant(nameEnd);
                var keyword = ReadIdentifierAt(cursor, out var keywordEnd);
                if (keyword != "in") return;

                cursor = NextSignificant(keywordEnd);
                var collection = ReadIdentifierAt(cursor, out _);
                if (collection != null && collections.TryGetValue(collection, out var kind))
                {
                    declared[name] = kind;
                    if (kind == MigrationMapping.TmpTypeKind.InputField)
                        inputFieldDeclarations.Add(nameStart);
                }
            }

            /// <summary>
            /// The TMP kind an expression produces, recognized only in the forms that actually
            /// carry a type: a component getter's type argument, a cast, <c>as</c>, and <c>new</c>.
            /// </summary>
            private MigrationMapping.TmpTypeKind? KindFromExpression(int start, int end)
            {
                for (var i = start; i < end && i < source.Length; i++)
                {
                    if (!isCode[i] || !IsIdentifierStart(source[i])) continue;
                    var word = ReadIdentifierAt(i, out var wordEnd);
                    if (word == null) { continue; }

                    if (MigrationMapping.ScriptTypes.TryGetValue(word, out var mapping))
                    {
                        var qualifier = NamespaceQualifierStart(i);
                        var before = PreviousSignificant((qualifier < 0 ? i : qualifier) - 1);
                        if (before >= 0)
                        {
                            var current = source[before];
                            if (current == '<' || current == '(')
                            {
                                if (current == '(' && !ClosesCast(wordEnd)) { i = wordEnd - 1; continue; }
                                return mapping.Kind;
                            }
                            var keyword = ReadIdentifierBefore(before, out _);
                            if (keyword == "as" || keyword == "new") return mapping.Kind;
                        }
                    }
                    i = wordEnd - 1;
                }
                return null;
            }

            /// <summary>Whether a parenthesized type is a cast rather than a call argument.</summary>
            private bool ClosesCast(int typeEnd)
            {
                var close = NextSignificant(typeEnd);
                return close < source.Length && source[close] == ')';
            }

            #endregion

            #region Types

            private void RewriteTypes()
            {
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (start, end) = identifiers[i];
                    var word = source.Substring(start, end - start);

                    if (MigrationMapping.ScriptTypes.TryGetValue(word, out var mapping))
                    {
                        var qualifier = NamespaceQualifierStart(start);
                        if (qualifier < 0 && IsMemberAccess(start)) continue;
                        if (mapping.Kind == MigrationMapping.TmpTypeKind.InputField)
                        {
                            var blocker = InputFieldTypeBlockReason(
                                qualifier < 0 ? start : qualifier, end);
                            if (blocker != null) AddWarning(start, end, blocker, true);
                        }
                        AddReplacement(qualifier < 0 ? start : qualifier, end,
                            qualifyLightSide
                                ? LightSideNamespace + "." + mapping.UniTextName
                                : mapping.UniTextName);
                        renamedAnything = true;
                        continue;
                    }

                    if (word == "TMPro" && !IsMemberAccess(start))
                    {
                        var dot = NextSignificant(end);
                        if (dot >= source.Length || source[dot] != '.') continue;
                        var qualified = ReadIdentifierAt(NextSignificant(dot + 1), out _);
                        if (qualified == null || !MigrationMapping.ScriptTypes.ContainsKey(qualified))
                            needsTmProUsing = true;
                        continue;
                    }

                    if (!MigrationMapping.UnmappedScriptTypes.TryGetValue(word, out var advice)) continue;
                    needsTmProUsing = true;
                    if (unmappedSeen.Add(word))
                        AddWarning(start, end, $"{word}: {advice}");
                }
            }

            private string InputFieldTypeBlockReason(int start, int end)
            {
                var before = PreviousSignificant(start - 1);
                var after = NextSignificant(end);
                if (after < source.Length && source[after] == ',')
                    return "TMP_InputField appears in a generic or tuple position the rewrite " +
                           "cannot bind to receivers; migrate this use by hand";
                if (before >= 0 && source[before] == ':')
                    return "A namespace-alias-qualified TMP_InputField is outside the rewrite's " +
                           "verified type forms; migrate this use by hand";

                if (before >= 0 && source[before] == '=')
                {
                    var aliasEnd = PreviousSignificant(before - 1);
                    ReadIdentifierBefore(aliasEnd, out var aliasStart);
                    if (ReadIdentifierBefore(PreviousSignificant(aliasStart - 1), out _) == "using")
                        return "A TMP_InputField using alias hides the receiver type from the " +
                               "rewrite; migrate this file by hand";
                }

                var keyword = ReadIdentifierBefore(before, out _);
                if (keyword == "static")
                    return "using static TMP_InputField is outside the rewrite's verified type " +
                           "forms; migrate this file by hand";
                if (before >= 0 && source[before] == '(')
                {
                    var typeOperatorEnd = PreviousSignificant(before - 1);
                    var typeOperator = ReadIdentifierBefore(typeOperatorEnd, out var typeOperatorStart);
                    var callOpen = PreviousSignificant(typeOperatorStart - 1);
                    var call = callOpen >= 0 && source[callOpen] == '('
                        ? ReadIdentifierBefore(PreviousSignificant(callOpen - 1), out _)
                        : null;
                    if (typeOperator == "typeof" && call == "AddComponent")
                        return "AddComponent(typeof(TMP_InputField)) cannot build " +
                               "UniTextEditable's required component composition; migrate it by hand";
                }
                if (keyword == "new")
                {
                    if (after >= source.Length || source[after] != '[')
                        return "TMP_InputField construction cannot create UniTextEditable's " +
                               "required component composition; migrate it by hand";
                }

                if (IsInputFieldBaseType(start))
                    return "UniTextEditable is sealed, so a TMP_InputField base type cannot be " +
                           "rewritten; migrate this type by hand";

                if (before >= 0 && source[before] == '<')
                {
                    var close = NextSignificant(end);
                    var callEnd = PreviousSignificant(before - 1);
                    var call = ReadIdentifierBefore(callEnd, out _);
                    var callOpen = close < source.Length
                        ? NextSignificant(close + 1)
                        : source.Length;
                    if (close < source.Length && source[close] == '>' &&
                        callOpen < source.Length && source[callOpen] == '(' &&
                        call == "AddComponent")
                        return "AddComponent<TMP_InputField> cannot build UniTextEditable's " +
                               "required component composition; migrate it by hand";
                }

                return null;
            }

            private bool IsInputFieldBaseType(int start)
            {
                var colonSeen = false;
                for (var i = PreviousSignificant(start - 1); i >= 0; i--)
                {
                    if (!isCode[i] || char.IsWhiteSpace(source[i])) continue;
                    var current = source[i];
                    if (current == ':')
                    {
                        if ((i > 0 && source[i - 1] == ':') ||
                            (i + 1 < source.Length && source[i + 1] == ':'))
                            continue;
                        colonSeen = true;
                        continue;
                    }
                    if (IsIdentifierPart(current))
                    {
                        var word = ReadIdentifierBefore(i, out var wordStart);
                        if (colonSeen &&
                            (word == "class" || word == "interface" || word == "record" ||
                             word == "where"))
                            return true;
                        i = wordStart;
                        continue;
                    }
                    if (current == '=' || current == ';' || current == '{' || current == '}' ||
                        current == '(' || current == ')' || current == '?' || current == '[' ||
                        current == ']')
                        return false;
                }
                return false;
            }

            private bool IsMemberAccess(int identifierStart)
            {
                var before = PreviousSignificant(identifierStart - 1);
                return before >= 0 && source[before] == '.';
            }

            /// <summary>
            /// Start of the <c>TMPro</c> qualifier written in front of a type, so
            /// <c>TMPro.TextMeshProUGUI</c> is replaced whole instead of leaving a namespace that
            /// no longer holds the type. Returns -1 when there is no such qualifier.
            /// </summary>
            private int NamespaceQualifierStart(int typeStart)
            {
                var dot = PreviousSignificant(typeStart - 1);
                if (dot < 0 || source[dot] != '.') return -1;
                var qualifier = ReadIdentifierBefore(PreviousSignificant(dot - 1), out var qualifierStart);
                if (qualifier != "TMPro") return -1;
                var before = PreviousSignificant(qualifierStart - 1);
                return before >= 0 && source[before] == '.' ? -1 : qualifierStart;
            }

            #endregion

            #region Members

            private void RewriteMembers()
            {
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (start, end) = identifiers[i];
                    var before = PreviousSignificant(start - 1);
                    if (before < 0 || source[before] != '.') continue;

                    var kind = ResolveReceiver(before);
                    if (!kind.HasValue)
                    {
                        var unresolved = source.Substring(start, end - start);
                        if (ReceiverMentionsInputField(before, start, end))
                        {
                            AddWarning(start, end,
                                $".{unresolved} — the TMP_InputField receiver is not in a shape " +
                                "the rewrite can resolve; migrate this use by hand", true);
                        }
                        else if (MigrationMapping.TextMembers.TryGetValue(
                                     unresolved, out var unresolvedMapping) &&
                                 unresolvedMapping.IsAutomatic &&
                                 unresolvedSeen.Add(unresolved))
                        {
                            AddWarning(start, end,
                                $".{unresolved} — the receiver is declared outside this file, so the " +
                                "rewrite cannot tell whether it holds a TMP text; if it does, rename " +
                                $"this to .{unresolvedMapping.Replacement} by hand");
                        }
                        continue;
                    }

                    var member = source.Substring(start, end - start);
                    var isInputField = kind.Value == MigrationMapping.TmpTypeKind.InputField;
                    var isFontAsset = kind.Value == MigrationMapping.TmpTypeKind.FontAsset;
                    var table = isInputField ? MigrationMapping.InputFieldMembers
                        : isFontAsset ? MigrationMapping.FontAssetMembers
                        : MigrationMapping.TextMembers;
                    if (!table.TryGetValue(member, out var mapping))
                    {
                        if (isInputField && !SurvivesRename(typeof(UniTextEditable), member))
                        {
                            AddWarning(start, end,
                                $".{member} — the rewrite has no verified UniTextEditable " +
                                "mapping for this member; migrate this input-field use by hand",
                                true);
                        }
                        else if (isFontAsset && !SurvivesRename(typeof(UniTextFont), member))
                        {
                            AddWarning(start, end,
                                $".{member} — the rewrite has no verified UniTextFont mapping " +
                                "for this member; migrate this font-asset use by hand", true);
                        }
                        continue;
                    }

                    if (mapping.IsAutomatic) AddReplacement(start, end, mapping.Replacement);
                    else AddWarning(start, end, $".{member} — {mapping.Advice}",
                        isInputField || isFontAsset);
                }
            }

            private void ReportUnresolvedInputReferences()
            {
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (start, end) = identifiers[i];
                    if (inputFieldDeclarations.Contains(start)) continue;

                    var name = source.Substring(start, end - start);
                    var isValue = declared.TryGetValue(name, out var kind) &&
                                  kind == MigrationMapping.TmpTypeKind.InputField;
                    var isCollection = collections.TryGetValue(name, out kind) &&
                                       kind == MigrationMapping.TmpTypeKind.InputField;
                    if (!isValue && !isCollection) continue;
                    var resolvedAccess = isCollection
                        ? IsResolvedInputCollectionAccess(end)
                        : FeedsMemberAccess(end);
                    if (resolvedAccess || IsNullComparison(start, end) ||
                        IsResolvedInputAssignment(end) ||
                        (isCollection && IsForeachCollection(start)))
                        continue;

                    AddWarning(start, end,
                        $"{name} — this TMP_InputField reference leaves the forms the rewrite can " +
                        "prove; migrate the use by hand", true);
                }
            }

            private bool FeedsMemberAccess(int end)
            {
                var next = NextSignificant(end);
                while (next < source.Length && (source[next] == '?' || source[next] == '!'))
                    next = NextSignificant(next + 1);
                if (next >= source.Length) return false;
                if (source[next] == '.') return true;
                if (source[next] != '[' && source[next] != '(') return false;

                var close = MatchForward(next, source[next], source[next] == '[' ? ']' : ')');
                if (close < 0) return false;
                next = NextSignificant(close + 1);
                while (next < source.Length && (source[next] == '?' || source[next] == '!'))
                    next = NextSignificant(next + 1);
                return next < source.Length && source[next] == '.';
            }

            private bool IsResolvedInputCollectionAccess(int end)
            {
                var next = NextSignificant(end);
                while (next < source.Length && (source[next] == '?' || source[next] == '!'))
                    next = NextSignificant(next + 1);
                if (next >= source.Length) return false;
                if (source[next] == '.')
                {
                    var member = ReadIdentifierAt(NextSignificant(next + 1), out _);
                    return member == "Count" || member == "Length";
                }
                if (source[next] != '[') return false;

                var close = MatchForward(next, '[', ']');
                if (close < 0) return false;
                next = NextSignificant(close + 1);
                while (next < source.Length && (source[next] == '?' || source[next] == '!'))
                    next = NextSignificant(next + 1);
                return next < source.Length && source[next] == '.';
            }

            private bool IsNullComparison(int start, int end)
            {
                var next = NextSignificant(end);
                if (next < source.Length && source[next] == '=')
                {
                    var second = NextSignificant(next + 1);
                    if (second < source.Length && source[second] == '=' &&
                        ReadIdentifierAt(NextSignificant(second + 1), out _) == "null")
                        return true;
                }
                else if (next < source.Length && source[next] == '!')
                {
                    var equals = NextSignificant(next + 1);
                    if (equals < source.Length && source[equals] == '=' &&
                        ReadIdentifierAt(NextSignificant(equals + 1), out _) == "null")
                        return true;
                }
                else if (ReadIdentifierAt(next, out var keywordEnd) == "is")
                {
                    var pattern = ReadIdentifierAt(NextSignificant(keywordEnd), out var patternEnd);
                    if (pattern == "null") return true;
                    if (pattern == "not" &&
                        ReadIdentifierAt(NextSignificant(patternEnd), out _) == "null")
                        return true;
                }

                var operatorEnd = PreviousSignificant(start - 1);
                if (operatorEnd < 0 || source[operatorEnd] != '=') return false;
                var operatorStart = PreviousSignificant(operatorEnd - 1);
                if (operatorStart < 0 ||
                    (source[operatorStart] != '=' && source[operatorStart] != '!'))
                    return false;
                return ReadIdentifierBefore(PreviousSignificant(operatorStart - 1), out _) == "null";
            }

            private bool IsResolvedInputAssignment(int end)
            {
                var assign = NextSignificant(end);
                if (assign >= source.Length || source[assign] != '=') return false;
                var next = NextSignificant(assign + 1);
                if (next < source.Length && (source[next] == '=' || source[next] == '>')) return false;
                if (ReadIdentifierAt(next, out var nullEnd) == "null")
                {
                    var terminator = NextSignificant(nullEnd);
                    if (terminator >= source.Length || source[terminator] == ';' ||
                        source[terminator] == ',' || source[terminator] == ')')
                        return true;
                }
                var initializerEnd = SkipInitializer(assign + 1);
                return KindFromExpression(assign + 1, initializerEnd) ==
                       MigrationMapping.TmpTypeKind.InputField;
            }

            private bool IsForeachCollection(int start)
            {
                var before = PreviousSignificant(start - 1);
                return ReadIdentifierBefore(before, out _) == "in";
            }

            /// <summary>
            /// Whether the receiver contains an input-field value whose resulting type the lexical
            /// rewrite cannot prove, such as a call or chained expression.
            /// </summary>
            private bool ReceiverMentionsInputField(int dotIndex, int memberStart, int memberEnd)
            {
                if (SurvivesRename(typeof(UniTextEditable),
                        source.Substring(memberStart, memberEnd - memberStart)))
                    return false;
                var end = PreviousSignificant(dotIndex - 1);
                while (end >= 0 && (source[end] == '?' || source[end] == '!'))
                    end = PreviousSignificant(end - 1);
                if (end < 0) return false;
                var start = ReceiverExpressionStart(end);
                if (start < 0) return false;

                var complex = source[end] == ')' || source[end] == ']';
                var inputFieldSeen = false;
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (identifierStart, identifierEnd) = identifiers[i];
                    if (identifierStart < start) continue;
                    if (identifierStart > end) break;

                    var word = source.Substring(identifierStart, identifierEnd - identifierStart);
                    if (word == "TMP_InputField")
                    {
                        inputFieldSeen = true;
                        continue;
                    }
                    if (declared.TryGetValue(word, out var kind) &&
                        kind == MigrationMapping.TmpTypeKind.InputField)
                    {
                        inputFieldSeen = true;
                        continue;
                    }
                    if (complex && collections.TryGetValue(word, out kind) &&
                        kind == MigrationMapping.TmpTypeKind.InputField)
                    {
                        inputFieldSeen = true;
                        continue;
                    }
                    if (!inputFieldSeen ||
                        !MigrationMapping.InputFieldMembers.TryGetValue(word, out var mapping))
                        continue;
                    if (mapping.IsAutomatic) return false;
                    return true;
                }
                return inputFieldSeen;
            }

            private int ReceiverExpressionStart(int end)
            {
                if (source[end] == ')')
                {
                    var open = MatchBackward(end, '(', ')');
                    if (open < 0) return end;
                    var callEnd = PreviousSignificant(open - 1);
                    if (callEnd < 0) return open;
                    if (source[callEnd] == '>')
                    {
                        var genericOpen = MatchBackward(callEnd, '<', '>');
                        if (genericOpen < 0) return open;
                        callEnd = PreviousSignificant(genericOpen - 1);
                    }
                    var call = ReadIdentifierBefore(callEnd, out var callStart);
                    if (call == null) return open;
                    var separator = PreviousSignificant(callStart - 1);
                    if (separator < 0 || source[separator] != '.') return callStart;
                    var callOwner = PreviousSignificant(separator - 1);
                    return callOwner < 0 ? callStart : ReceiverExpressionStart(callOwner);
                }

                if (source[end] == ']')
                {
                    var open = MatchBackward(end, '[', ']');
                    if (open < 0) return end;
                    var indexOwner = PreviousSignificant(open - 1);
                    return indexOwner < 0 ? open : ReceiverExpressionStart(indexOwner);
                }

                var name = ReadIdentifierBefore(end, out var nameStart);
                if (name == null) return end;
                var before = PreviousSignificant(nameStart - 1);
                if (before < 0 || source[before] != '.') return nameStart;
                var owner = PreviousSignificant(before - 1);
                return owner < 0 ? nameStart : ReceiverExpressionStart(owner);
            }

            /// <summary>
            /// The TMP kind of the expression ending at <paramref name="dotIndex"/>, resolved from
            /// declarations in this file plus the expression forms that carry a type themselves.
            /// </summary>
            private MigrationMapping.TmpTypeKind? ResolveReceiver(int dotIndex)
            {
                var i = PreviousSignificant(dotIndex - 1);
                while (i >= 0 && (source[i] == '?' || source[i] == '!'))
                    i = PreviousSignificant(i - 1);
                if (i < 0) return null;

                if (source[i] == ']')
                {
                    var open = MatchBackward(i, '[', ']');
                    if (open < 0) return null;
                    i = PreviousSignificant(open - 1);
                    if (i < 0) return null;
                    var indexed = ReadIdentifierBefore(i, out _);
                    return indexed != null && collections.TryGetValue(indexed, out var elementKind)
                        ? elementKind
                        : null;
                }

                if (source[i] == ')')
                {
                    var open = MatchBackward(i, '(', ')');
                    if (open < 0) return null;
                    var callKind = KindFromCall(open);
                    if (callKind.HasValue) return callKind;
                    return KindFromExpression(open + 1, i);
                }

                var name = ReadIdentifierBefore(i, out var nameStart);
                if (name == null) return null;
                if (name == "this" || name == "base")
                {
                    var owner = PreviousSignificant(nameStart - 1);
                    if (owner >= 0 && source[owner] == '.')
                    {
                        var chained = ReadIdentifierBefore(PreviousSignificant(owner - 1), out _);
                        if (chained != null && declared.TryGetValue(chained, out var chainedKind))
                            return chainedKind;
                    }
                    return null;
                }
                if (declared.TryGetValue(name, out var kind)) return kind;
                // A static receiver: TMP_FontAsset.CreateFontAsset(...) names the type itself.
                return MigrationMapping.ScriptTypes.TryGetValue(name, out var typeMapping)
                    ? typeMapping.Kind
                    : (MigrationMapping.TmpTypeKind?)null;
            }

            /// <summary>The kind a component getter's type argument names, for <c>GetComponent&lt;T&gt;()</c>.</summary>
            private MigrationMapping.TmpTypeKind? KindFromCall(int parenIndex)
            {
                var i = PreviousSignificant(parenIndex - 1);
                if (i < 0 || source[i] != '>') return null;
                var open = MatchBackward(i, '<', '>');
                if (open < 0) return null;

                var argument = source.Substring(open + 1, i - open - 1).Trim();
                var qualifier = argument.LastIndexOf('.');
                if (qualifier >= 0) argument = argument.Substring(qualifier + 1).Trim();
                if (!MigrationMapping.ScriptTypes.TryGetValue(argument, out var mapping)) return null;

                var callEnd = PreviousSignificant(open - 1);
                var call = ReadIdentifierBefore(callEnd, out _);
                return call != null && componentGetters.Contains(call) ? mapping.Kind : null;
            }

            #endregion

            #region Usings

            private void RewriteUsings()
            {
                FindUsings();
                if (usingTmProStart >= 0)
                {
                    if (needsTmProUsing)
                    {
                        AddWarning(usingTmProStart, usingTmProEnd,
                            "using TMPro; stays — this file still names TMPro types with no UniText " +
                            "counterpart");
                    }
                    else if (!hasLightSideUsing && !qualifyLightSide)
                    {
                        AddReplacement(usingTmProStart, usingTmProEnd, "using LightSide;");
                        hasLightSideUsing = true;
                        return;
                    }
                    else
                    {
                        AddReplacement(usingTmProStart, usingTmProEnd, string.Empty);
                    }
                }

                if (qualifyLightSide)
                {
                    if (renamedAnything)
                        AddWarning(collidingStart, collidingEnd,
                            $"{collidingName} already names a type in this file, so UniText types " +
                            "are written qualified rather than adding using LightSide;");
                    return;
                }

                if (!renamedAnything || hasLightSideUsing) return;

                if (lastUsingLineEnd < 0)
                {
                    // Nothing to hang the directive on: a file that only ever wrote TMPro.Type.
                    AddReplacement(0, 0, source.IndexOf('\r') >= 0
                        ? "using LightSide;\r\n"
                        : "using LightSide;\n");
                    return;
                }

                var carriageReturn = lastUsingLineEnd > 0 && source[lastUsingLineEnd - 1] == '\r';
                var anchor = carriageReturn ? lastUsingLineEnd - 1 : lastUsingLineEnd;
                AddReplacement(anchor, anchor,
                    carriageReturn ? "\r\nusing LightSide;" : "\nusing LightSide;");
            }

            private void FindUsings()
            {
                for (var i = 0; i < identifiers.Count; i++)
                {
                    var (start, end) = identifiers[i];
                    if (source.Substring(start, end - start) != "using") continue;
                    if (IsMemberAccess(start)) continue;

                    var nameStart = NextSignificant(end);
                    var name = ReadIdentifierAt(nameStart, out var nameEnd);
                    if (name == null) continue;

                    var terminator = NextSignificant(nameEnd);
                    if (terminator >= source.Length || source[terminator] != ';') continue;

                    lastUsingLineEnd = EndOfLine(terminator);
                    if (name == "LightSide") hasLightSideUsing = true;
                    else if (name == "TMPro")
                    {
                        usingTmProStart = start;
                        usingTmProEnd = terminator + 1;
                    }
                }
            }

            private int EndOfLine(int index)
            {
                var i = index;
                while (i < source.Length && source[i] != '\n') i++;
                return i;
            }

            #endregion

            private void ReportConditionalRegions()
            {
                for (var i = 0; i < conditionalRegions.Count; i++)
                {
                    var (start, end) = conditionalRegions[i];
                    var containsInputField = source.IndexOf("TMP_InputField", start,
                        end - start, StringComparison.Ordinal) >= 0;
                    AddWarning(start, EndOfLine(start), containsInputField
                            ? "TMP-conditional region contains TMP_InputField and stays untouched; " +
                              "migrate this region by hand"
                            : "TMP-conditional region left untouched — which branch compiles is the " +
                              "symbol's decision, so migrate it by hand",
                        containsInputField);
                }
            }

            #region Emission

            private int LineOf(int index)
            {
                var low = 0;
                var high = lineStarts.Length - 1;
                while (low < high)
                {
                    var middle = (low + high + 1) / 2;
                    if (lineStarts[middle] <= index) low = middle;
                    else high = middle - 1;
                }
                return low;
            }

            private void AddReplacement(int start, int end, string replacement)
            {
                var line = LineOf(start);
                if (line != LineOf(Math.Max(start, end - 1))) return;
                Results.Add(new ScriptReplacement
                {
                    lineNumber = line + 1,
                    columnStart = start - lineStarts[line],
                    columnEnd = end - lineStarts[line],
                    original = source.Substring(start, end - start),
                    replacement = replacement,
                    isSelected = true,
                });
            }

            private void AddWarning(int start, int end, string message, bool blocksFile = false)
            {
                var line = LineOf(start);
                Results.Add(new ScriptReplacement
                {
                    lineNumber = line + 1,
                    columnStart = start - lineStarts[line],
                    columnEnd = Math.Min(end, EndOfLine(start)) - lineStarts[line],
                    original = source.Substring(start, Math.Max(0, Math.Min(end, EndOfLine(start)) - start)),
                    replacement = null,
                    isWarningOnly = true,
                    blocksFile = blocksFile,
                    warningMessage = message,
                    isSelected = false,
                });
            }

            #endregion
        }
    }
}
