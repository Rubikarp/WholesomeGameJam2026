using System;
using System.Text;

namespace LightSide.Samples
{
    internal sealed class MathSlide : BasicUsageSlide
    {
        private static readonly string text = BuildText();

        private const string OrdSymbols = @"\alpha \beta \gamma \delta \epsilon \zeta \eta \theta \iota \kappa \lambda \mu \nu \xi \omicron \pi \rho \sigma \tau \upsilon \phi \chi \psi \omega \varepsilon \vartheta \varpi \varrho \varsigma \varphi \varkappa \digamma \Gamma \Delta \Theta \Lambda \Xi \Pi \Sigma \Upsilon \Phi \Psi \Omega \Alpha \Beta \Epsilon \Zeta \Eta \Iota \Kappa \Mu \Nu \Omicron \Rho \Tau \Chi \infty \partial \nabla \forall \exists \nexists \emptyset \varnothing \neg \lnot \top \bot \angle \measuredangle \sphericalangle \triangle \vartriangle \triangledown \surd \prime \backprime \hbar \hslash \ell \wp \Re \Im \aleph \beth \gimel \daleth \complement \eth \mho \Finv \Game \imath \jmath \square \Box \blacksquare \lozenge \Diamond \blacklozenge \blacktriangle \blacktriangledown \blacktriangleleft \blacktriangleright \bigstar \circledS \circledR \diagup \diagdown \clubsuit \diamondsuit \heartsuit \spadesuit \flat \natural \sharp \checkmark \maltese \degree \vdots \Vert \vert \quad \qquad \enspace \thinspace \negthinspace";

        private const string BinarySymbols = @"\pm \mp \times \div \cdot \ast \star \circ \bullet \diamond \cap \cup \sqcap \sqcup \vee \lor \wedge \land \setminus \wr \oplus \ominus \otimes \oslash \odot \uplus \amalg \dagger \ddagger \bigcirc \bigtriangleup \bigtriangledown \triangleleft \triangleright \lhd \rhd \unlhd \unrhd \dotplus \smallsetminus \Cap \doublecap \Cup \doublecup \barwedge \veebar \doublebarwedge \boxminus \boxplus \boxtimes \boxdot \circleddash \circledast \circledcirc \centerdot \intercal \divideontimes \ltimes \rtimes \leftthreetimes \rightthreetimes \curlywedge \curlyvee \lessdot \gtrdot";

        private const string RelationSymbols = @"\leq \le \geq \ge \neq \ne \approx \equiv \sim \simeq \cong \subset \supset \subseteq \supseteq \in \ni \owns \notin \mid \parallel \perp \propto \asymp \doteq \prec \succ \preceq \succeq \ll \gg \vdash \dashv \models \smile \frown \bowtie \sqsubseteq \sqsupseteq \leqq \leqslant \eqslantless \lesssim \lessapprox \approxeq \lessgtr \lesseqgtr \lesseqqgtr \doteqdot \Doteq \risingdotseq \fallingdotseq \backsim \backsimeq \subseteqq \Subset \sqsubset \preccurlyeq \curlyeqprec \precsim \precapprox \vartriangleleft \trianglelefteq \vDash \Vdash \Vvdash \smallsmile \smallfrown \bumpeq \Bumpeq \geqq \geqslant \eqslantgtr \gtrsim \gtrapprox \gtrless \gtreqless \gtreqqless \eqcirc \circeq \triangleq \thicksim \thickapprox \supseteqq \Supset \sqsupset \succcurlyeq \curlyeqsucc \succsim \succapprox \vartriangleright \trianglerighteq \shortmid \shortparallel \between \pitchfork \varpropto \therefore \because \eqsim \Join \lll \llless \ggg \gggtr \backepsilon \nless \ngtr \nleq \ngeq \lneq \gneq \lneqq \gneqq \lnsim \gnsim \lnapprox \gnapprox \nprec \nsucc \npreceq \nsucceq \precnsim \succnsim \precnapprox \succnapprox \precneqq \succneqq \nsim \ncong \nmid \nparallel \nvdash \nvDash \nVdash \nVDash \ntriangleleft \ntriangleright \ntrianglelefteq \ntrianglerighteq \subsetneq \supsetneq \subsetneqq \supsetneqq \nsubseteq \nsupseteq";

        private const string ArrowSymbols = @"\leftarrow \gets \rightarrow \to \leftrightarrow \uparrow \downarrow \updownarrow \Leftarrow \Rightarrow \Leftrightarrow \Uparrow \Downarrow \Updownarrow \longleftarrow \longrightarrow \longleftrightarrow \Longleftarrow \Longrightarrow \Longleftrightarrow \mapsto \longmapsto \nearrow \searrow \swarrow \nwarrow \hookleftarrow \hookrightarrow \leftharpoonup \leftharpoondown \rightharpoonup \rightharpoondown \rightleftharpoons \leftrightharpoons \dashrightarrow \dashleftarrow \leftleftarrows \leftrightarrows \Lleftarrow \twoheadleftarrow \leftarrowtail \looparrowleft \curvearrowleft \circlearrowleft \Lsh \upuparrows \upharpoonleft \downharpoonleft \rightrightarrows \rightleftarrows \twoheadrightarrow \rightarrowtail \looparrowright \curvearrowright \circlearrowright \Rsh \downdownarrows \upharpoonright \restriction \downharpoonright \rightsquigarrow \leadsto \leftrightsquigarrow \Rrightarrow \multimap \origof \imageof \nleftarrow \nrightarrow \nLeftarrow \nRightarrow \nleftrightarrow \nLeftrightarrow";

        private const string OperatorSymbols = @"\sum \prod \coprod \int \intop \iint \iiint \oint \oiint \oiiint \smallint \bigcap \bigcup \bigsqcup \bigvee \bigwedge \bigodot \bigoplus \bigotimes \biguplus \sin \cos \tan \cot \sec \csc \arcsin \arccos \arctan \sinh \cosh \tanh \coth \log \ln \lg \exp \lim \liminf \limsup \sup \inf \min \max \det \dim \ker \hom \arg \deg \gcd \Pr \mod";

        private const string DelimiterSymbols = @"\langle \lfloor \lceil \lbrace \lbrack \lparen \lvert \lVert \lgroup \lmoustache \ulcorner \llcorner \rangle \rfloor \rceil \rbrace \rbrack \rparen \rvert \rVert \rgroup \rmoustache \urcorner \lrcorner";

        private const string PunctuationSymbols = @"\ldotp \cdotp \colon \semicolon \ldots \cdots \ddots";

        public override string Text => text;

        public override void Register(BasicUsageExampleBase example)
        {
            example.AddStyle(new MathModifier
            {
                MathFont = example.MathFont,
                Mode = MathMode.Display
            }, new MathParseRule());
        }

        private static string BuildText()
        {
            var builder = new StringBuilder(24_000);
            builder.Append("<b>OpenType MATH — complete parser/layout stress sheet</b>\n\n");

            Section(builder, "Atoms, spacing and scripts");
            Formula(builder, @"a+b-c\times d\div e=12.34,\quad -x+(-y)");
            Formula(builder, @"x^2\quad a_i\quad x_i^2\quad x^2_i\quad x^{y^{z_1}}\quad {}^nC_r");
            Formula(builder, @"f'\quad f''\quad f'''\quad {f'}^2\quad T^{i_1\ldots i_n}_{j_1\ldots j_m}");
            Formula(builder, @"\mathrm{d}x\quad \textrm{roman text}\quad \text{words inside math}\quad \operatorname{rank} A");
            Formula(builder, @"x_{\text{label}}\quad y^{\text{max}}\quad \text{base}^{n}_{i}\quad \left(\text{words}\right)^2");
            Formula(builder, @"\operatorname*{argmax}_{x\in X} f(x)\quad \operatorname{erf}(x)");
            Formula(builder, @"\%\ \_\ \#\ \$\ \&\ \{\ \}\ \|\ \backslash");
            Formula(builder, @"a\,b\:c\;d\!e\ f\quad g\qquad h\enspace i\thinspace j\medspace k\thickspace l");
            Formula(builder, @"a\negthinspace b\negmedspace c\negthickspace d\hspace{7mu}e\hspace{0.5em}f\hspace{-3mu}g");
            Formula(builder, "a+b % this comment is ignored\n+c ~ d");

            Section(builder, "Fractions, binomials, radicals and styles");
            Formula(builder, @"\frac{a}{b}\quad \dfrac{1}{1+\frac{x}{y}}\quad \tfrac{n-1}{n}\quad \cfrac{1}{1+\cfrac{1}{x}}");
            Formula(builder, @"\binom{n}{k}\quad \dbinom{2n}{n}\quad \tbinom{r}{2}\quad \frac{}{x}\quad \frac{x}{}");
            Formula(builder, @"\sqrt{x}\quad \sqrt[3]{x+y}\quad \sqrt[n+1]{\frac{a}{b}}\quad \sqrt{1+\sqrt{1+\sqrt{x}}}");
            Formula(builder, @"\displaystyle\frac{a}{b}\quad \textstyle\frac{a}{b}\quad \scriptstyle\frac{a}{b}\quad \scriptscriptstyle\frac{a}{b}");

            Section(builder, "Operators and limits");
            Formula(builder, @"\sum_{i=1}^{n}i^2\quad \prod_{p\in P}p\quad \coprod_{j=0}^{m}A_j");
            Formula(builder, @"\int_0^1x^2\,\mathrm{d}x\quad \iint_D f\,\mathrm{d}A\quad \iiint_V\rho\,\mathrm{d}V\quad \oint_C F\cdot\mathrm{d}r");
            Formula(builder, @"\sum\limits_{i=1}^{n}i\quad \sum\nolimits_{i=1}^{n}i\quad \sum\displaylimits_{i=1}^{n}i");
            Formula(builder, @"\lim_{x\to0}\frac{\sin x}{x}=1\quad \min_{x\in X}f(x)\quad \max_{y\in Y}g(y)");
            Formula(builder, @"\bigcap_{i=1}^{n}A_i\quad \bigcup_{i=1}^{n}A_i\quad \bigoplus_{k=0}^{m}V_k");
            Formula(builder, @"z^2\quad Z_2\quad \left(\frac{a}{b}\right)^2\quad \left(\frac{a}{b}\right)_i^j");

            Section(builder, "Stretchy delimiters, accents and rules");
            Formula(builder, @"\left(\frac{a+b}{c+d}\right)\quad \left[\sqrt{\frac{x}{y}}\right]\quad \left\{\frac{n}{k}\right\}");
            Formula(builder, @"\left\langle\frac{x}{y}\right\rangle\quad \left\lfloor\frac{x}{y}\right\rfloor\quad \left\lceil\frac{x}{y}\right\rceil");
            Formula(builder, @"\left\lvert\frac{x}{y}\right\rvert\quad \left\lVert\frac{x}{y}\right\rVert\quad \left.\frac{\mathrm{d}f}{\mathrm{d}x}\right|_{x=0}");
            Formula(builder, @"\left\uparrow\frac{x}{y}\right\downarrow\quad \left\Uparrow\frac{x}{y}\right\Downarrow\quad \left/\frac{x}{y}\right\backslash");
            Formula(builder, @"\left\updownarrow\frac{x}{y}\right\updownarrow\quad \left\Updownarrow\frac{x}{y}\right\Updownarrow");
            Formula(builder, @"\left<\frac{x}{y}\right>\quad \left|\frac{x}{y}\right|");
            Formula(builder, @"\left\lgroup\frac{x}{y}\right\rgroup\quad \left\lmoustache\frac{x}{y}\right\rmoustache");
            Formula(builder, @"\left\ulcorner\frac{x}{y}\right\urcorner\quad \left\llcorner\frac{x}{y}\right\lrcorner");
            Formula(builder, @"\left\lparen\frac{x}{y}\right\rparen\quad \left\lbrack\frac{x}{y}\right\rbrack");
            Formula(builder, @"\hat{x}\ \widehat{xyz}\ \check{x}\ \tilde{x}\ \widetilde{xyz}\ \acute{x}\ \grave{x}");
            Formula(builder, @"\dot{x}\ \ddot{x}\ \dddot{x}\ \ddddot{x}\ \breve{x}\ \bar{x}\ \vec{x}\ \mathring{x}");
            Formula(builder, @"\hat{x}\quad \hat{X}\quad \hat{\frac{a}{b}}\quad \widehat{\frac{a+b}{c+d}}");
            Formula(builder, @"\overline{x+y}\quad \underline{x+y}\quad \overline{\underline{\frac{a}{b}}}");

            Section(builder, "Composition with text modifiers");
            builder.Append("<size=60%><math>\\frac{a+b}{\\sqrt{x}}</math></size> ")
                .Append("<math>\\frac{a+b}{\\sqrt{x}}</math> ")
                .Append("<size=160%><math>\\frac{a+b}{\\sqrt{x}}</math></size>\n");
            builder.Append("<color=#55D6BE><math>\\left(\\sum_{i=0}^{n}x_i\\right)^2</math></color> ")
                .Append("<color=#FF6B6B><math>\\overline{a+b}=\\sqrt{c}</math></color>\n");

            Section(builder, "Matrices and aligned environments");
            Formula(builder, @"\begin{matrix}a&b\\c&d\end{matrix}\quad \begin{pmatrix}1&2\\3&4\end{pmatrix}\quad \begin{bmatrix}x&y\\z&w\end{bmatrix}");
            Formula(builder, @"\begin{Bmatrix}a&b\\c&d\end{Bmatrix}\quad \begin{vmatrix}a&b\\c&d\end{vmatrix}\quad \begin{Vmatrix}x&y\\z&w\end{Vmatrix}");
            Formula(builder, @"f(x)=\begin{cases}x^2&x\ge0\\-x&x<0\end{cases}");
            Formula(builder, @"\begin{aligned}a+b&=c\\x&=y+z\end{aligned}\quad \begin{align}u&=v\\p+q&=r\end{align}");
            Formula(builder, @"\begin{split}A&=B+C\\&=D+E\end{split}\quad \begin{gathered}x+y=z\\a=b\end{gathered}\quad \begin{gather}m=n\\r=s\end{gather}");

            Section(builder, "Nested construction stress");
            Formula(builder, @"\left[\frac{\sum_{i=1}^{n}\sqrt[i+1]{x_i^2+1}}{\prod_{j=1}^{m}(1+y_j)}\right]_{n\to\infty}");
            Formula(builder, @"\sqrt[\frac{p}{q}]{\frac{\left(\sum_{k=0}^{n}a_kx^k\right)^2}{1+\left\lVert v\right\rVert^2}}");
            Formula(builder, @"\begin{pmatrix}\dfrac{a_1}{b_1}&\sqrt{x^2+y^2}\\\sum_{i=1}^{n}i&\displaystyle\int_0^1t\,\mathrm{d}t\end{pmatrix}");
            Formula(builder, @"𝑥+𝑦=𝛼\quad ∀x∈ℝ, x^2≥0\quad A⊆B⇒A∪B=B");

            Section(builder, "Every registered ordinary symbol command");
            Wall(builder, OrdSymbols);
            Section(builder, "Every registered binary operator command");
            Wall(builder, BinarySymbols);
            Section(builder, "Every registered relation command");
            Wall(builder, RelationSymbols);
            Section(builder, "Every registered arrow command");
            Wall(builder, ArrowSymbols);
            Section(builder, "Every registered large/named operator command");
            Wall(builder, OperatorSymbols);
            Section(builder, "Every registered delimiter and punctuation command");
            Wall(builder, DelimiterSymbols);
            Wall(builder, PunctuationSymbols);

            return builder.ToString();
        }

        private static void Section(StringBuilder builder, string title)
            => builder.Append("\n<b>").Append(title).Append("</b>\n");

        private static void Formula(StringBuilder builder, string formula)
            => builder.Append("<math>").Append(formula).Append("</math>\n");

        private static void Wall(StringBuilder builder, string commands)
        {
            const int commandsPerLine = 11;
            var entries = commands.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var start = 0; start < entries.Length; start += commandsPerLine)
            {
                builder.Append("<math>");
                var end = Math.Min(start + commandsPerLine, entries.Length);
                for (var i = start; i < end; i++)
                {
                    if (i != start) builder.Append(@"\quad ");
                    builder.Append(entries[i]);
                }
                builder.Append("</math>\n");
            }
        }
    }
}
