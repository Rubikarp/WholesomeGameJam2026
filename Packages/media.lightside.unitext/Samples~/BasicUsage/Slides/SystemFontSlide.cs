namespace LightSide.Samples
{
    internal sealed class SystemFontSlide : BasicUsageSlide
    {
        public override string Text =>
            "\U0001F5A5 <b>System Font Fallback</b> \u2014 <b>Font</b> and <b>Font Stack</b> are null\n\n" +
            @"<b=,r>MIXED</b> <i=r>SCRIPTS</i>, ONE LINE
Hello Привет 你好 こんにちは 안녕 שלום مرحبا नमस्ते สวัสดี ⵙⵍⴰⵎ ᎣᏏᏲ ᐃᓄᒃᑎᑐᑦ

CJK RARE (Extension B / C / D, U+20000+)
𠀋 𡃁 𢑘 𣗋 𤴓 𥋝 𦫿 𧌒 𨋢 𩸽 𪜈 𪛇 𫝆 𫠠
Variants: <var=900>龍/龙 馬/马 體/体 鬱 憂 蘿 蔔 籠 籬</var>

ANCIENT / HISTORIC SCRIPTS (supplementary plane)
Egyptian Hieroglyphs: 𓂀 𓆣 𓊪 𓏏 𓎛 𓇋 𓅓𓊵 𓏏𓊪 𓆇𓆑
Cuneiform: 𒀀 𒁲 𒂗 𒃶 𒅗 𒈦 𒋗 𒍢 𒍝 𒋗𒁉𒅖
Linear B (Mycenaean): 𐀀 𐀁 𐀂 𐀃 𐀄 𐀅 𐀆 𐀇 𐀈 𐀉
Phoenician: 𐤀 𐤁 𐤂 𐤃 𐤄 𐤅 𐤆 𐤇 𐤈 𐤉
Old Persian: 𐎠 𐎡 𐎢 𐎣 𐎤 𐎥 𐎦 𐎧
Old Italic: 𐌀 𐌁 𐌂 𐌃 𐌄 𐌅 𐌆 𐌇
Gothic: 𐌰 𐌱 𐌲 𐌳 𐌴 𐌵 𐌶
Glagolitic: ⰀⰁⰂⰃⰄⰅⰆⰇ
Runic (Elder Futhark): ᚠ ᚢ ᚦ ᚨ ᚱ ᚲ ᚷ ᚹ ᚺ ᚾ ᛁ ᛃ ᛇ ᛈ ᛉ ᛊ ᛏ ᛒ ᛖ ᛗ ᛚ ᛜ ᛞ ᛟ
Ogham: ᚁ ᚂ ᚃ ᚄ ᚅ ᚆ ᚇ ᚈ ᚉ
Brahmi (Ashokan): 𑀅 𑀆 𑀇 𑀈 𑀉 𑀊
Tifinagh (Berber): ⵃ ⵄ ⵅ ⵆ ⵇ ⵈ ⵉ ⵊ ⵋ
Cherokee: Ꭰ Ꭱ Ꭲ Ꭳ Ꭴ Ꭵ Ꭶ Ꭷ
Canadian Syllabics: ᐃ ᐅ ᐆ ᐊ ᐋ ᐱ ᐲ ᐳ ᐴ
Mongolian (vertical): ᠮ ᠣ ᠩ ᠭ ᠣ ᠯ ᠬ ᠡ ᠯ ᠡ
Tibetan stacks: བོད་སྐད་ ལ་ མཁས་པ ་ བཀའ་འགྱུར

INDIC COMPLEX CLUSTERS (conjuncts, virama, vowel signs)
Hindi (Devanagari): कर्तव्य निष्ठा ज्ञान संस्कृत क्षत्रिय श्री
Marathi: मराठी साहित्य आणि संस्कृती
Tamil: க்ஷத்ரியன் ஸ்ரீ தமிழ் இலக்கியம்
Telugu: శ్రీ తెలుగు సాహిత్యం క్షత్రియ
Kannada: ಕನ್ನಡ ಸಾಹಿತ್ಯ ಕ್ಷತ್ರಿಯ
Malayalam: മലയാളം സാഹിത്യം ക്ഷത്രിയൻ
Bengali: বাংলা সাহিত্য ক্ষত্রিয় শ্রী
Gurmukhi (Punjabi): ਪੰਜਾਬੀ ਸਾਹਿਤ ਖ਼ਾਲਸਾ
Gujarati: ગુજરાતી સાહિત્ય ક્ષત્રિય
Odia: ଓଡ଼ିଆ ସାହିତ୍ୟ କ୍ଷତ୍ରିୟ
Sinhala: සිංහල සාහිත්‍යය ක්‍ෂත්‍රිය

SOUTHEAST ASIAN (tones, subscripts, stacks)
Thai (5 tones): ก้า ก่า ก๊า ก๋า กา — ราคา ฿250 อาหารไทย
Lao: ສະບາຍດີ ປະເທດລາວ ສະບາຍ
Khmer (coeng subscripts): ខ្ញុំ ស្រឡាញ់ កម្ពុជា ប្រទេស
Myanmar (Burmese): မြန်မာဘာသာ နှင့် ယဉ်ကျေးမှု
Javanese: ꦗꦮ ꦕꦫꦏꦤ꧀
Balinese: ᬩᬮᬶ ᬅᬓ᭄ᬱᬭ
Sundanese: ᮞᮥᮔ᮪ᮓ ᮃᮊ᮪ᮞᮛ

RIGHT-TO-LEFT (with diacritics / cantillation)
Arabic harakat: مَحَبَّةٌ كَامِلَةٌ — مَكْتَبَةٌ صَغِيرَةٌ
Arabic-Indic digits: ٠١٢٣٤٥٦٧٨٩
Persian: کتابخانهٔ بزرگ تهران ۱۲۳۴۵
Urdu: کتاب کا مطالعہ
Hebrew vowels: שָׁלוֹם עוֹלָם הַיּוֹם
Hebrew cantillation: בְּרֵאשִׁ֖ית בָּרָ֣א אֱלֹהִ֑ים אֵ֥ת הַשָּׁמַ֖יִם וְאֵ֥ת הָאָֽרֶץ׃
Yiddish: אַ גוטן טאָג, ייִדישע ליטעראַטור
Syriac: ܫܠܡܐ ܠܟ
N'Ko: ߒߞߏ ߦߋ߫ ߞߊߓߊ߫
Adlam (Fulani): 𞤀 𞤁 𞤂 𞤃 𞤄 𞤅 𞤆

EAST AFRICAN / OTHER AFRICAN
Ethiopic (Amharic/Ge'ez): ኢትዮጵያ ሰላም ቋንቋ
Tigrinya: ሰላም ናጻ ሃገር
Bamum: ꚠ ꚡ ꚢ ꚣ
Vai: ꔀ ꔁ ꔂ ꔃ ꔄ

MATHEMATICS / SCIENCE
∀x∈ℝ ∃y∈ℕ : ∫₀^∞ e^(-x²)dx = √π/2
∇·E = ρ/ε₀, ∮B·dl = μ₀I + μ₀ε₀ ∂Φ_E/∂t
αβγδεζηθικλμνξοπρστυφχψω ΑΒΓΔΕΖΗΘ
ℵ₀ ℶ ℷ ℸ ⅏ ℘ ℧ Ω ∞ ∅ ∂ ∇ √ ∛ ∜
⨋ ⨌ ⨎ ⨏ ⨐ ⨑ ⨒ ⨓ ⨔ ⨕ (uncommon integrals)
𝒜 ℬ 𝒞 𝒟 ℰ ℱ 𝒢 (script math letters)
𝔄 𝔅 ℭ 𝔇 𝔈 𝔉 𝔊 ℌ ℑ (fraktur math)
𝟬 𝟭 𝟮 𝟯 𝟰 𝟱 𝟲 𝟳 𝟴 𝟵 (sans-serif bold digits)

MUSIC / GAMES
Music: 𝄞 𝄢 𝅘𝅥 𝅗𝅥 𝅘𝅥𝅮 ♩ ♪ ♫ ♬ ♭ ♮ ♯
Cards: 🂠 🂡 🂢 🂣 🂤 🂥 🃁 🃂 🃃 🃔 🃕
Dominoes: 🀱 🀲 🀳 🀴 🀵 🀶 🀷
Mahjong: 🀀 🀁 🀂 🀃 🀅 🀆 🀇 🀈 🀉 🀝 🀟 🀫
Chess: ♔ ♕ ♖ ♗ ♘ ♙ ♚ ♛ ♜ ♝ ♞ ♟ 🨂 🨃

GEOMETRIC / BOX DRAWING
╔══╤══╗
║▓▒░║   ◢◣◤◥   ◐◑◒◓   ▲△▴▵   ◇◈◉○●
╚══╧══╝

COMPLEX EMOJI ZWJ SEQUENCES
Family: 👨‍👩‍👧‍👦  👨‍👨‍👧‍👧  👩‍👩‍👦‍👦
Kiss with skin tones: 👨🏻‍❤️‍💋‍👨🏿  👩🏼‍❤️‍💋‍👨🏾
Profession (gender-neutral + skin): 🧑🏽‍🚀 🧑🏼‍⚕️ 🧑🏿‍🎓 🧑🏻‍🍳 🧑🏾‍🏫 🧑🏼‍🔬 🧑🏻‍🎨 🧑🏿‍💻
Flags with modifiers: 🏳️‍🌈 🏳️‍⚧️ 🏴‍☠️
Animal ZWJ: 🐈‍⬛ 🐻‍❄️ 🐦‍⬛ 🐦‍🔥
Regional flags via tag sequences: 🏴󠁧󠁢󠁳󠁣󠁴󠁿 🏴󠁧󠁢󠁥󠁮󠁧󠁿 🏴󠁧󠁢󠁷󠁬󠁳󠁿 🏴󠁵󠁳󠁴󠁸󠁿 🏴󠁵󠁳󠁣󠁡󠁿 🏴󠁵󠁳󠁮󠁹󠁿
Rare country flags: 🇦🇶 🇰🇾 🇼🇸 🇹🇲 🇧🇶 🇨🇽 🇪🇭 🇫🇰 🇬🇸 🇸🇭

PHONETIC IPA + TONES
[ˈfoʊ̯nɪmɪk t͡ɹænsˈkɹɪpʃn̩]
Click consonants: ʘ ǀ ǁ ǂ ǃ ŋǂ ŋǁ kǃ k͡ǀ
Tone letters: a˥ a˩ a˧˥ a˨˩˦ a̋ á ǎ â à ɑ̃ːˤ
Suprasegmentals: ˈˌːˑ˞̩̪̺̻̟̠̘̙̜̝̞̥̬̃̆̌̂̏̄

VARIATION SELECTORS (text vs emoji presentation)
Text: ☺︎ ▲︎ ♠︎ ♣︎ ♥︎ ♦︎ ☎︎ ✉︎ ☀︎
Emoji: ☺️ ▲️ ♠️ ♣️ ♥️ ♦️ ☎️ ✉️ ☀️

NORMALIZATION TESTS
NFC: café é Å Ω
NFD: cafe + ́, e + ́, A + ̊, Ω + (no combine)
Multi-mark: ậ (a + ̂ + ̣), ḗ (e + ̄ + ́)

ZALGO (combining mark stress)
h̢͕̟͙̪̟͙̬̪͡e̢͚̥̦͔l̢͚̲̮̱͔l͖̱͍͔̕o̢̬̱̟̩
T̴̢̛̛̮̞̳̘͕̗̜̥̭͑͒̀̌̈̉͊̕̕ḩ̷̱̩̱̟̘͊̌̃̉̅̑̃̾̊͝ę̸̢̞̗̜͙̗̦̟̅̾̆̃̕

BIDI MIND-BENDERS
""He said: «مرحبا 12345 שלום», then 7+8=15.""
Mixed numerals: 5 + ٥ + ५ + ៥ + ๕ + ໕ + ၅ = 35
LTR-in-RTL: العربية English הברית עברית

LATIN EXTENDED RARE
Vietnamese tones: à á ả ã ạ ằ ắ ẳ ẵ ặ ầ ấ ẩ ẫ ậ
Esperanto: Saluton! Mi ŝatas ĉokoladon kaj ĝisrenkonton.
Polish: Zażółć gęślą jaźń.
Czech: Příliš žluťoučký kůň úpěl ďábelské ódy.
Slovak: Ľubľaňa krížom-krážom — jedľa s diakritikou.
Romanian: Așa-i că-mi place ștefan?
Turkish: İstanbul'da güneş doğdu çığlık atan İğne.
Welsh: Ll, ll, ŵ, ŷ. Llanfairpwllgwyngyll.
Icelandic: Ég, þú, hann, ð, þ.
Faroese: Tjóðveldi
Sami: Sápmi Sámegiella ŋ Č Š Ŧ Đ Á

CURRENCIES + SYMBOLS
$ € £ ¥ ₹ ₽ ₩ ₪ ₺ ₴ ₸ ₼ ₾ ﷼ ฿ ₿ Ƀ Ł ⏣ ⎔
Special: ™ ® © ℠ ℗ № ‰ ‱ ¶ § ¤
Punctuation variants: – — ― ‒ … ‥ ‹ › « » „ """" """" '' ''
CJK punctuation: 、。「」『』〈〉《》【】〔〕

WHITESPACE / INVISIBLE
Spaces: [ ] [ ] [ ] [ ] [ ] [ ] [ ] [ ]
(en/em/thin/hair/non-break/zwsp/zwnj/zwj invisible above)

ARROWS / RARE SYMBOLS
↹ ⇄ ⇌ ⇎ ⇏ ⇙ ⇚ ⇛ ⇜ ⇝ ⇞ ⇟ ⇪ ⇫ ⇬ ⇭ ⇮ ⇯
⫷ ⫸ ⫹ ⫺ ⫻ ⫽ ⫾ ⫿ ⬀ ⬁ ⬂ ⬃ ⬄ ⬅︎ ⬆︎ ⬇︎
";

        private UniTextFont font;
        private UniTextFontStack fontStack;
        private bool systemFontDisabled;

        public override void Register(BasicUsageExampleBase example)
        {
            example.AddStyle(new VariationModifier(), new TagRule("var"));
        }

        public override void Enter(BasicUsageExampleBase example)
        {
            font = example.DemoText.Font;
            fontStack = example.DemoText.FontStack;
            systemFontDisabled = LightSide.SystemFont.Disabled;
            LightSide.SystemFont.Disabled = false;
            example.DemoText.Font = null;
            example.DemoText.FontStack = null;
        }

        public override void Exit(BasicUsageExampleBase example)
        {
            example.DemoText.FontStack = fontStack;
            example.DemoText.Font = font;
            LightSide.SystemFont.Disabled = systemFontDisabled;
        }
    }
}
